"""Small stdin/stdout adapter for the bundled rars archive engine."""

import base64
import json
import os
import stat
import sys

import rars


class RarRebuildRequiredError(Exception):
    pass


def archive_name(value):
    return value.replace("\\", "/").strip("/")


def check_source(path):
    info = os.lstat(path)
    flags = getattr(info, "st_file_attributes", 0)
    if stat.S_ISLNK(info.st_mode) or flags & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 1024):
        raise ValueError("Folders containing links or junctions cannot be added.")
    return info


def add_inputs(builder, inputs, existing):
    """Merge new paths without overwriting existing archive members."""
    for item in inputs:
        path, name = item["path"], archive_name(item["name"])
        info = check_source(path)
        if not name:
            raise ValueError("An archive entry needs a name.")
        if not stat.S_ISDIR(info.st_mode):
            add_file(builder, path, name, existing)
            continue
        def raise_walk_error(error):
            raise error
        for folder, dirs, files in os.walk(path, followlinks=False, onerror=raise_walk_error):
            dirs.sort(key=str.casefold)
            files.sort(key=str.casefold)
            check_source(folder)
            rel = os.path.relpath(folder, path)
            member = name if rel == "." else name + "/" + archive_name(rel)
            key = member.casefold()
            if key in existing and not existing[key]:
                raise ValueError("A file already exists where a folder is needed: " + member)
            if key not in existing:
                builder.add_directory(member)
                existing[key] = True
            for child in dirs:
                check_source(os.path.join(folder, child))
            for child in files:
                source = os.path.join(folder, child)
                check_source(source)
                add_file(builder, source, member + "/" + child, existing)


def add_file(builder, path, name, existing):
    key = archive_name(name).casefold()
    if key in existing:
        raise ValueError("An item with this name already exists in the archive: " + name)
    builder.add(path, arcname=archive_name(name))
    existing[key] = False


def create(request):
    password = request.get("password") or None
    level = min(5, max(0, int(request.get("level", 3))))
    split = request.get("splitBytes") or None
    builder = rars.RarBuilder(format="rar50", compression=level, store=level == 0,
                              password=password, encrypt_headers=bool(request.get("hideNames")),
                              volume_size=split)
    add_inputs(builder, request["inputs"], {})
    if split:
        builder.write_volumes(request["output"])
    else:
        builder.write(request["output"])


def edit(request):
    archive = rars.RarFile(request["archive"], password=request.get("password") or None)
    issues = archive.rewrite_preservation_issues()
    infos = archive.infolist()
    if issues:
        if archive.needs_password or any(item.is_encrypted for item in infos):
            raise ValueError("This protected archive has features BeniZip cannot safely preserve. It was not changed.")
        if any(item.is_split_before or item.is_split_after for item in infos):
            raise ValueError("Editing split RAR archives is not supported. The archive was not changed.")
        if not request.get("allowRebuild", False):
            raise RarRebuildRequiredError(", ".join(issues))
    builder = rars.RarBuilder.from_archive(archive, preserve=not bool(issues), staging_dir=request["stageDir"])
    ids = builder.member_ids()
    if len(infos) != len(ids):
        raise ValueError("Archive members could not be safely mapped for editing.")
    existing = {}
    for info in infos:
        key = archive_name(info.filename).casefold()
        if not key or key in existing:
            raise ValueError("The archive contains ambiguous member names.")
        existing[key] = info.is_dir()
    for name in list(existing):
        parts = name.split("/")
        for end in range(1, len(parts)):
            parent = "/".join(parts[:end])
            if parent in existing and not existing[parent]:
                raise ValueError("The archive contains an item that is both a file and a folder.")
            existing.setdefault(parent, True)
    if request["operation"] == "delete":
        selected = [archive_name(item).casefold() for item in request["members"]]
        matches = [(info.filename, item_id) for info, item_id in zip(infos, ids)
                   if any(archive_name(info.filename).casefold() == name or
                          archive_name(info.filename).casefold().startswith(name + "/")
                          for name in selected)]
        if not matches:
            raise ValueError("The selected archive items could not be found.")
        for _, item_id in sorted(matches, key=lambda item: item[0].count("/"), reverse=True):
            builder.remove(item_id)
    else:
        add_inputs(builder, request["inputs"], existing)
    builder.write(request["output"])


def main():
    request = json.loads(base64.b64decode(sys.stdin.readline()).decode("utf-8"))
    if request["operation"] == "create":
        create(request)
    elif request["operation"] in ("add", "delete"):
        edit(request)
    else:
        raise ValueError("Unknown RAR operation.")


if __name__ == "__main__":
    try:
        main()
        print(json.dumps({"ok": True}))
    except Exception as error:
        print(json.dumps({"ok": False, "error": str(error), "kind": type(error).__name__}))
        sys.exit(1)
