# PaddleOCR experimental pipeline notice

D4Hub's optional, local-only combat text experiment uses the following NuGet
packages and upstream projects. The product default remains Windows OCR unless
an explicit experiment selects Paddle.

## PaddleSharp and PaddleOCR packages

- `Sdcb.PaddleOCR` 3.3.1
- `Sdcb.PaddleOCR.Models.Local` 3.3.1
- `Sdcb.PaddleOCR.Models.LocalV5` 3.0.0
- `Sdcb.PaddleOCR.Models.Shared` 2.7.0.1
- `Sdcb.PaddleInference` 3.3.1
- `Sdcb.PaddleInference.runtime.win64.mkl` 3.3.1.70
- Project: https://github.com/sdcb/PaddleSharp
- Package repository commit for `Sdcb.PaddleOCR` 3.3.1:
  `139bc184a6d86c0c60bb8b8a90fb641b21c0b0e6`
- License declared by the packages: Apache-2.0
- License source: https://github.com/sdcb/PaddleSharp/blob/master/LICENSE
- `Sdcb.PaddleOCR.Models.Local` 3.3.1 nupkg SHA-256:
  `182EA2ABF9A19FC3D8C9F51F30300409D5BD45A06E3BEFBF27DCA8585577CD71`
- `Sdcb.PaddleOCR.Models.LocalV5` 3.0.0 nupkg SHA-256:
  `1C11EE72F2FFACE29B5891BEFAB4C95494AF4411230A52908137AF8587C8412D`

The local model packages contain PaddleOCR model data. D4Hub does not download
models at runtime.

## PaddleOCR and PaddlePaddle

- PaddleOCR: https://github.com/PaddlePaddle/PaddleOCR
- PaddleOCR license: https://github.com/PaddlePaddle/PaddleOCR/blob/main/LICENSE
- PaddlePaddle: https://github.com/PaddlePaddle/Paddle
- PaddlePaddle license: https://github.com/PaddlePaddle/Paddle/blob/develop/LICENSE
- License: Apache-2.0

## OpenCvSharp and OpenCV

- `OpenCvSharp4` 4.11.0.20250507
- `OpenCvSharp4.runtime.win` 4.11.0.20250507
- OpenCvSharp: https://github.com/shimat/opencvsharp
- OpenCvSharp license:
  https://github.com/shimat/opencvsharp/blob/master/LICENSE
- OpenCV 4.11 runtime components are redistributed by the runtime package.
- OpenCV: https://github.com/opencv/opencv
- OpenCV license: https://github.com/opencv/opencv/blob/4.x/LICENSE
- License: Apache-2.0

The managed and native OpenCvSharp package versions are intentionally identical
to avoid an unsupported ABI combination.

## License texts

The Apache License 2.0 text used by the projects above is included at
`LICENSE-APACHE-2.0.txt` in this directory. D4Hub's tracking, restricted damage
parser, quality receipts, and fallback behavior are D4Hub-specific code and do
not modify the upstream projects.
