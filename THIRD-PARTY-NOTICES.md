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

RAR creation and editing are optional. They use the separately installed
RAR command-line helper, under its own licence. BeniZip does not redistribute
RAR/WinRAR binaries, source, icons, or artwork.
