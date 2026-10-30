# Release notes

## 0.2.2 — 30 October 2026

- Create RAR5 archives and add or remove items from supported RAR archives without WinRAR.
- Create encrypted and split RAR archives. Protected RAR editing requires the password and temporarily stages decrypted data on disk.
- Unsupported protected or split archives are left unchanged. For other archives that require rebuilding, BeniZip asks first and keeps a copy of the original.

## 0.2.1 — 29 October 2026

- Fixed clipped text in the address bar, search box and other text fields.

## 0.2.0 — 19 October 2026

- First public, open-source release under the MIT licence.
- Compact white Windows interface with a familiar menu, toolbar, address bar and file table; remembers window size.
- Extraction progress with current file, cancel, elapsed time, estimated speed and remaining time.
- Installer with all 13 supported archive types selected by default, Explorer commands, shortcuts and Windows default-app confirmation where required.
- Editable archive-type selection and actual default status inside the app.
- Automatic GitHub update discovery and signed, checksum-verified installation.
- ZIP, 7z and optional RAR creation/editing, password handling, multipart support, integrity testing, selected extraction and read-only previews.
- Archive editing preserves encryption and rejects missing or incorrect passwords.
- Published engine licence notices and matching 7-Zip source archive.

Windows protects existing default-app choices; confirmation in Windows can still be required. RAR creation/editing requires a separately installed licensed RAR helper. Archive repair and SFX authoring are outside this release.
