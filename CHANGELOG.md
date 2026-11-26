# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Initial release: background removal for CD, DVD and Blu-ray scans in 1-pass (over white) and
  2-pass (over white and black), as a WinUI 3 app with tabs, Before / After, backdrops, Batch Mode
  with automatic 2-pass pairing, and a command line in the same `Lychee.exe`.
- Turn the disc by dragging with the right button on the stage, Before and After alike; exported and copied
  turned, and saved turned from Batch Mode's Preview.
- Auto Rotation: discs are turned so their print reads level, from their print read with Windows' OCR
  (left as scanned when it can't tell): in the app as the stage's angle, in Batch Mode and on the command line
  in the saved PNG.
- Windows installer.

[Unreleased]: https://github.com/rlaphoenix/Lychee/commits/main
