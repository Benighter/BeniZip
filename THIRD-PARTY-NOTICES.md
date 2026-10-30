# Third-party components

BeniZip's original application source is MIT licensed. It calls an unmodified
7-Zip command-line engine as a separate process. The MIT licence does not
replace the licences of that engine.

The Windows distribution includes 7z.exe and 7z.dll from 7-Zip 26.04,
Copyright (c) 1999-2026 Igor Pavlov. Parts are under GNU LGPL 2.1 or later,
BSD 2-Clause or BSD 3-Clause terms; the RAR decompressor has the additional
unRAR restriction. The complete original notices are in vendor/7zip/License.txt
in this repository and engine/License.txt in the installed application.

Engine source for this exact version:
https://github.com/ip7z/7zip/releases/download/26.04/7z2604-src.tar.xz

The matching source archive is also provided with the BeniZip GitHub release.
Source SHA-256: 9691944c0fe0d01bb49373a704fb983fd33bc98b1738695179dfbf99ac1734f6

7-Zip project and licensing: https://www.7-zip.org/license.txt

RAR creation and editing use rars 0.12.3, an independent implementation at
https://github.com/bitplane/rars, distributed by its authors under the
Apache License 2.0. Its dependency list is in vendor/rars/rars-sbom.json;
component licence texts and notices are in vendor/rars/licenses. The bundled
adapter includes Python 3.12.10 under the Python Software Foundation licence
and a PyInstaller 6.21.0 bootloader under its licence and bootloader exception.
It also includes OpenSSL 3.0.16, Copyright (c) 1998-2025 The OpenSSL Project
and Copyright (c) 1995-1998 Eric A. Young and Tim J. Hudson, under Apache 2.0.
Their licence texts and the rars licence are in vendor/rars/LICENSE-*.txt.
These notices are also included in the installed engine/License.txt.
