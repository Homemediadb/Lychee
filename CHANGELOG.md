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
- Auto Rotation: discs are turned so their print reads level, from their print read with Windows' OCR and
  checked with its spell checker (left as scanned when it can't tell): in the app as the stage's angle, in
  Batch Mode and on the command line in the saved PNG. In Settings and on the command line it can be turned
  off, read in another installed OCR language, or limited to small turns.
- Scans with more than one disc on them: each disc is found and gets its own result, in reading order. The app
  shows a tab for each under the scan's tab, Before framing where the chosen disc is cut from the scan; Batch
  Mode gives each disc a row under the scan's name; the command line saves `IMAGE_disc1_nobg.png` and so on.
- Progress says each step (reading the background, finding the discs, each disc's edge, hole, background, crop
  and auto rotation) and shows the disc as each step leaves it, on the stage and in Batch Mode's rows; the stage
  can't be turned by hand until auto rotation is done.
- Windows installer.
- Links to the project on GitHub (github.com/Homemediadb/Lychee): the About page links to it, its license,
  issues and third-party notices, F1 opens it, and `Lychee --help` ends with its address.

[Unreleased]: https://github.com/Homemediadb/Lychee/commits/main
