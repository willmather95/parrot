# Speech quality fixtures

These short clips form a small regression suite for English dictation. They do
not measure generalized speech recognition quality.

- `parakeet-phoebe-0` comes from the sherpa-onnx export of NVIDIA's NeMo
  Parakeet TDT 0.6B v2 model. The repository declares the fixture under
  [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). The sherpa-onnx
  documentation publishes its reference transcription.
- `librispeech-1089-134686-0001` and `librispeech-1221-135766-0001` are
  LibriSpeech test-clean utterances redistributed with a sherpa-onnx Zipformer
  model. LibriSpeech is licensed under
  [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) and was prepared by
  Vassil Panayotov with the assistance of Daniel Povey. The audio derives from
  public-domain LibriVox recordings. The model repository identifies its model
  package as Apache-2.0; that metadata does not replace the corpus attribution.

Source and integrity details are recorded in `fixtures.json`. The downloaded
files are not committed to this repository.
