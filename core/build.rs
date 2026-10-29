fn main() {
    println!("cargo:rerun-if-changed=../proto/audio.proto");
    let descriptors = protox::compile(["../proto/audio.proto"], ["../proto"]).unwrap();
    let mut config = prost_build::Config::new();
    config.type_attribute(".", "#[derive(serde::Serialize, serde::Deserialize)]");
    config.message_attribute(".", "#[serde(default)]");
    config.compile_fds(descriptors).unwrap();
}
