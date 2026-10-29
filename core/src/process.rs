use std::{
    io::Read,
    os::windows::{io::AsRawHandle, process::CommandExt},
    path::Path,
    process::{Command, Stdio},
    sync::{
        Arc, Mutex,
        atomic::{AtomicBool, Ordering},
    },
    thread,
    time::{Duration, Instant},
};
use windows_sys::Win32::{
    Foundation::{CloseHandle, HANDLE},
    System::JobObjects::*,
};

struct Job(HANDLE);
impl Job {
    fn attach(child: &std::process::Child) -> Result<Self, String> {
        unsafe {
            let handle = CreateJobObjectW(std::ptr::null(), std::ptr::null());
            if handle.is_null() {
                return Err(std::io::Error::last_os_error().to_string());
            }
            let job = Self(handle);
            let mut limits: JOBOBJECT_EXTENDED_LIMIT_INFORMATION = std::mem::zeroed();
            limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
            if SetInformationJobObject(
                handle,
                JobObjectExtendedLimitInformation,
                &limits as *const _ as *const _,
                std::mem::size_of_val(&limits) as u32,
            ) == 0
                || AssignProcessToJobObject(handle, child.as_raw_handle()) == 0
            {
                return Err(std::io::Error::last_os_error().to_string());
            }
            Ok(job)
        }
    }
}
impl Drop for Job {
    fn drop(&mut self) {
        unsafe {
            CloseHandle(self.0);
        }
    }
}

pub fn run(
    exe: &Path,
    args: &[String],
    cancel: &AtomicBool,
    timeout: Duration,
) -> Result<String, String> {
    run_allowed(exe, args, cancel, timeout, &[0])
}

pub fn run_allowed(
    exe: &Path,
    args: &[String],
    cancel: &AtomicBool,
    timeout: Duration,
    allowed: &[i32],
) -> Result<String, String> {
    if cancel.load(Ordering::Acquire) {
        return Err("Cancelled".into());
    }
    let mut child = Command::new(exe)
        .args(args)
        .creation_flags(0x08000000)
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .map_err(|e| format!("Cannot run {}: {e}", exe.display()))?;
    let job = match Job::attach(&child) {
        Ok(job) => job,
        Err(e) => {
            let _ = child.kill();
            let _ = child.wait();
            return Err(format!("Cannot supervise audio tool: {e}"));
        }
    };
    let output = Arc::new(Mutex::new(Vec::<u8>::new()));
    let readers: Vec<_> = [
        Box::new(child.stdout.take().unwrap()) as Box<dyn Read + Send>,
        Box::new(child.stderr.take().unwrap()),
    ]
    .into_iter()
    .map(|mut pipe| {
        let output = output.clone();
        thread::spawn(move || {
            let mut buffer = [0; 8192];
            while let Ok(n) = pipe.read(&mut buffer) {
                if n == 0 {
                    break;
                }
                let mut bytes = output.lock().unwrap();
                bytes.extend_from_slice(&buffer[..n]);
                if bytes.len() > 1_048_576 {
                    let excess = bytes.len() - 1_048_576;
                    bytes.drain(..excess);
                }
            }
        })
    })
    .collect();
    let start = Instant::now();
    let outcome = loop {
        if cancel.load(Ordering::Acquire) {
            let _ = child.kill();
            break Err("Cancelled".to_string());
        }
        if start.elapsed() > timeout {
            let _ = child.kill();
            break Err(format!("{} timed out", exe.display()));
        }
        match child.try_wait() {
            Ok(Some(status)) => break Ok(status),
            Ok(None) => thread::sleep(Duration::from_millis(30)),
            Err(e) => {
                let _ = child.kill();
                break Err(e.to_string());
            }
        }
    };
    let _ = child.wait();
    drop(job);
    for reader in readers {
        let _ = reader.join();
    }
    let bytes = output.lock().unwrap();
    let text = String::from_utf8_lossy(&bytes).into_owned();
    match outcome {
        Ok(status) if status.code().is_some_and(|code| allowed.contains(&code)) => Ok(text),
        Ok(status) => Err(format!("{} exited with {status}:\n{text}", exe.display())),
        Err(e) => Err(e),
    }
}
