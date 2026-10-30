# Bundled RAR adapter

`RarAdapter.zip` contains the compiled `src/RarAdapter.py` adapter, the
`rars` Python extension, and a Python/PyInstaller Windows runtime. It is a
build input embedded in `BeniZip.Cli.exe`; the runtime verifies its pinned
SHA-256 before extracting or running it.

- rars 0.12.3: https://github.com/bitplane/rars/releases/tag/v0.12.3
- Upstream Windows wheel `rars-0.12.3-cp310-abi3-win_amd64.whl` SHA-256:
  `5e926e8c861491a9673b8c0bb64e6c3099d50deaebd9633f52d9391c04e7463a`
- Python 3.12.10 and PyInstaller 6.21.0, built for 64-bit Windows.
- Bundled ZIP SHA-256:
  `5e8ceaf9ed9da4f8498ac7a87373e2b1af72e4101633a55cd10e18e7a28ec91d`

To rebuild the adapter, install the verified wheel in an isolated Python
environment with PyInstaller, then run PyInstaller in `--onedir` mode on
`src/RarAdapter.py` with the name `BeniZip.RarAdapter`. Zip the contents of
the resulting onedir folder, including `_internal`, and update the pinned
hash in `src/RarSupport.cs`. Re-run `tests/verify_rar.py` and the release
checks after rebuilding. PyInstaller output may differ across environments.

The engine's dependency inventory is `rars-sbom.json`; dependency licence
files are in `licenses`, with the engine, Python and PyInstaller licences in
the adjacent `LICENSE-*.txt` files. The Python runtime also includes OpenSSL
3.0.16; its licence is included there. `scripts/Build.ps1` includes those texts
in the installed application's `engine/License.txt`.
