#!/usr/bin/env python3
"""Generate the Solution extension using the selected Rider's rd-gen (no Gradle).

Dependencies are explicit, local inputs; this script never downloads or runs a
user project. See ../protocol/README.md for the verified SDK and commands.
"""
from __future__ import annotations

import argparse
import os
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]
PROTOCOL = ROOT / "rider/protocol"


def run(args: list[str | Path]) -> None:
    args = [str(a) for a in args]
    print("+ " + subprocess.list2cmdline(args), flush=True)
    subprocess.run(args, check=True, timeout=90)


def emit_jvm_jar(java: Path, rider: Path, compiler: Path, output: Path) -> None:
    """Compile only the checked-in protocol, without examples or generation tools."""
    runtime_jars = ["util-8.jar", "annotations.jar", "intellij.libraries.rd.core.jar",
                    "intellij.libraries.rd.framework.jar", "intellij.rd.ide.model.generated.jar"]
    classpath = os.pathsep.join(str(rider / "lib" / name) for name in runtime_jars)
    output = output.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    run([java, "-cp", str(compiler / "*"), "org.jetbrains.kotlin.cli.jvm.K2JVMCompiler",
         "-no-stdlib", "-no-reflect", "-jvm-target", "17", "-module-name", "anotherfsm_protocol",
         "-classpath", classpath, "-d", output, PROTOCOL / "generated/kotlin/AnotherFsmModel.Generated.kt"])
    print(f"JVM_JAR {output}", flush=True)


def verify(java: Path, rider: Path, compiler: Path, work: Path) -> None:
    # Kotlin accepts @argfiles, avoiding the Windows 32K command-line limit.
    arguments = ["-no-stdlib", "-no-reflect", "-jvm-target", "17", "-classpath",
                 os.pathsep.join(p.as_posix() for p in sorted((rider / "lib").glob("*.jar"))),
                 "-d", (work / "frontend").as_posix(),
                 (PROTOCOL / "generated/kotlin/AnotherFsmModel.Generated.kt").as_posix(),
                 (PROTOCOL / "examples/FrontendBinding.kt").as_posix()]
    argfile = work / "kotlin-args.txt"
    argfile.write_text("\n".join('"' + a + '"' for a in arguments), encoding="utf-8")
    run([java, "-cp", str(compiler / "*"), "org.jetbrains.kotlin.cli.jvm.K2JVMCompiler",
         "@" + str(argfile)])
    print("PASS Kotlin generated model + host Project binding", flush=True)
    run([rider / "jbr/bin/javac.exe", "--release", "17", "-cp",
         os.pathsep.join([str(work / "frontend"), str(rider / "lib/*")]),
         "-d", work / "java", PROTOCOL / "examples/FrontendJavaBinding.java"])
    print("PASS Java consumption + host Project binding", flush=True)
    run(["dotnet", "build", PROTOCOL / "Protocol.Compile.csproj", "--nologo",
         f"-p:RiderHome={rider.as_posix()}",
         f"-p:BaseIntermediateOutputPath={(work / 'csharp/obj').as_posix()}/",
         f"-p:OutputPath={(work / 'csharp/bin').as_posix()}/"])
    print("PASS C# generated model + host ISolution binding", flush=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rider-home", type=Path, required=True)
    parser.add_argument("--rider-model", type=Path,
                        help="Official SDK lib/rd/rider-model.jar (not a runtime model jar)")
    parser.add_argument("--kotlin-compiler-dir", type=Path, required=True,
                        help="Directory containing Kotlin compiler-embeddable and its dependencies")
    parser.add_argument("--work-dir", type=Path, default=ROOT / ".scratch/issue-32")
    parser.add_argument("--check", action="store_true", help="Compare generation without modifying tracked sources")
    parser.add_argument("--verify", action="store_true", help="Compile both generated ends and host binding examples")
    parser.add_argument("--jvm-jar", type=Path, help="Retain a production protocol JAR (no examples or bundled runtime)")
    parser.add_argument("--compile-jvm-only", action="store_true",
                        help="Compile checked-in Kotlin without generating; requires --jvm-jar, not --rider-model")
    args = parser.parse_args()
    if args.jvm_jar and args.jvm_jar.suffix != ".jar":
        parser.error("--jvm-jar must end in .jar")
    if args.compile_jvm_only and (not args.jvm_jar or args.check or args.verify):
        parser.error("--compile-jvm-only requires --jvm-jar and cannot combine with --check/--verify")
    if not args.compile_jvm_only and not args.rider_model:
        parser.error("Generation requires --rider-model")
    rider = args.rider_home.resolve()
    model = args.rider_model.resolve() if args.rider_model else None
    compiler = args.kotlin_compiler_dir.resolve()
    java = rider / "jbr/bin/java.exe"
    required = [java] if args.compile_jvm_only else [java, rider / "lib/rd-gen.jar", model]
    for path in required:
        if not path.is_file():
            parser.error(f"Required file not found: {path}")
    if not list(compiler.glob("kotlin-compiler-embeddable-*.jar")):
        parser.error(f"No Kotlin compiler-embeddable in {compiler}")
    if args.compile_jvm_only:
        emit_jvm_jar(java, rider, compiler, args.jvm_jar)
        return
    args.work_dir.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="rd-generate-", dir=args.work_dir.resolve()) as tmp:
        work = Path(tmp)
        classes = work / "classes"
        # kotlinc does not expand classpath wildcards. Keep generation independent
        # of the rest of the IDE and avoid the Windows command-line length limit.
        sdk_cp = os.pathsep.join(str(p) for p in [
            rider / "lib/rd-gen.jar", rider / "lib/util-8.jar",
            rider / "lib/intellij.libraries.rd.core.jar",
            rider / "lib/intellij.libraries.rd.framework.jar",
            rider / "lib/intellij.libraries.kotlin.reflect.jar",
            rider / "lib/annotations.jar", model,
        ])
        run([java, "-cp", str(compiler / "*"), "org.jetbrains.kotlin.cli.jvm.K2JVMCompiler",
             "-no-stdlib", "-no-reflect", "-jvm-target", "17", "-classpath", sdk_cp,
             "-d", classes, PROTOCOL / "model/AnotherFsmModel.kt"])
        specs = work / "generators.txt"
        specs.write_text("\n".join(
            "||".join([lang, flow, "com.jetbrains.rider.model.nova.ide.IdeRoot", ns,
                       str(work / lang), ".Generated", ""])
            for lang, flow, ns in [
                ("kotlin", "asis", "com.anotherfsm.rider.protocol"),
                ("csharp", "reversed", "AnotherFSM.Rider.Protocol"),
            ]) + "\n", encoding="utf-8")
        run([java, "-cp", os.pathsep.join([str(classes), sdk_cp]),
             "com.jetbrains.rd.generator.nova.MainKt", "-v", "-n", "-p", "model.anotherfsm",
             "-g", specs])
        expected = {"kotlin": "AnotherFsmModel.Generated.kt", "csharp": "AnotherFsmModel.Generated.cs"}
        for lang, filename in expected.items():
            files = list((work / lang).rglob("*.*"))
            if len(files) != 1 or files[0].name != filename:
                raise RuntimeError(f"Unexpected {lang} outputs: {files}")
            target = PROTOCOL / "generated" / lang / filename
            # Normalize newlines for reproducible checkouts on Windows and Unix.
            text = files[0].read_text(encoding="utf-8").replace("\r\n", "\n")
            if args.check:
                if not target.is_file() or target.read_text(encoding="utf-8") != text:
                    raise RuntimeError(f"Generated file differs: {target}")
                print(f"MATCH {target}", flush=True)
            else:
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_text(text, encoding="utf-8", newline="\n")
                print(f"GENERATED {target}", flush=True)
        if args.verify:
            verify(java, rider, compiler, work)
        if args.jvm_jar:
            emit_jvm_jar(java, rider, compiler, args.jvm_jar)


if __name__ == "__main__":
    main()
