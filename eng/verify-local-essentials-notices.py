#!/usr/bin/env python3
import io
import json
import sys
import zipfile
from pathlib import Path

package, assets_path, notices = map(Path, sys.argv[1:])
assets = json.loads(assets_path.read_text())


def verify(name, content):
    if (notices / name).read_bytes() != content:
        raise SystemExit(f"{name} differs from the restored native component; review its notices.")


def restored_package(identity):
    relative = assets["libraries"][identity]["path"]
    for folder in assets["packageFolders"]:
        directory = Path(folder) / relative
        if directory.is_dir():
            return directory
    raise SystemExit(f"Restored package not found: {identity}")


def archive_notice(directory, archive_name):
    archives = list(directory.rglob(archive_name))
    if len(archives) != 1:
        raise SystemExit(f"Expected one {archive_name} in {directory}, found {len(archives)}.")
    with zipfile.ZipFile(archives[0]) as archive:
        return archive.read("third_party_licenses.txt")


with zipfile.ZipFile(package) as nupkg:
    native = "lib/net10.0-android36.0/"
    for artifact, notice in [
        ("genai-prompt-1.0.0-beta5.aar", "NOTICE-MLKitPrompt.txt"),
        ("genai-common-1.0.0-beta5.aar", "NOTICE-MLKitCommon.txt"),
    ]:
        with zipfile.ZipFile(io.BytesIO(nupkg.read(native + artifact))) as aar:
            verify(notice, aar.read("third_party_licenses.txt"))
    with zipfile.ZipFile(io.BytesIO(nupkg.read(native + "Microsoft.Maui.Essentials.AI.aar"))) as aar:
        schema_path = "META-INF/third_party_licenses/com.google.mlkit/genai-schema/third_party_licenses.txt"
        schema_notices = []
        for jar_name in (name for name in aar.namelist() if name.endswith(".jar")):
            with zipfile.ZipFile(io.BytesIO(aar.read(jar_name))) as jar:
                if schema_path in jar.namelist():
                    schema_notices.append(jar.read(schema_path))
        if len(schema_notices) != 1:
            raise SystemExit(f"Expected one GenAI schema notice, found {len(schema_notices)}.")
        verify("NOTICE-MLKitSchema.txt", schema_notices[0])

mlkit = restored_package("Xamarin.Google.MLKit.Common/118.11.0.3")
transport = restored_package("Xamarin.Google.Android.DataTransport.TransportRuntime/4.0.0.3")
verify("LICENSE-AndroidXBindings.txt", (mlkit / "LICENSE.md").read_bytes())
verify("LICENSE-AndroidXBindings.txt", (transport / "LICENSE.md").read_bytes())
verify("NOTICE-MLKitTerms.txt", (mlkit / "THIRD-PARTY-NOTICES.txt").read_bytes())
verify("NOTICE-MLKitCore.txt", archive_notice(mlkit, "common.aar"))
verify("NOTICE-DataTransport.txt", archive_notice(transport, "transport-runtime.aar"))
print(f"Verified bundled Android notices against {package.name} and its restored dependencies.")
