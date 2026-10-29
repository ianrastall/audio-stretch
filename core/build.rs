fn main() {
    println!("cargo:rerun-if-changed=../proto/audio.proto");
    let descriptors = protox::compile(["../proto/audio.proto"], ["../proto"]).unwrap();
    prost_build::compile_fds(descriptors).unwrap();
}
