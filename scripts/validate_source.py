#!/usr/bin/env python3
"""Структурный аудит поставки. Не компилятор C# и не исполнение Core.Smoke."""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]

GENERATED_TOP_LEVEL = {'.git', '.cache', '.tools', 'artifacts'}
GENERATED_DIRECTORY_NAMES = {'bin', 'obj'}
GENERATED_BUILD_ASSETS = {
    'src/Mostik.Android/Resources/drawable-nodpi/google_translate_badge.png',
}

def is_source_file(path: Path) -> bool:
    relative = path.relative_to(ROOT)
    parts = relative.parts
    portable = relative.as_posix()
    return (
        parts[0] not in GENERATED_TOP_LEVEL
        and not any(part in GENERATED_DIRECTORY_NAMES for part in parts)
        and not portable.startswith('src/Mostik.Android/NativeLibs/')
        and portable not in GENERATED_BUILD_ASSETS
    )

def validate() -> list[str]:
    results: list[str] = []
    def check(value: bool, description: str) -> None:
        if not value:
            raise AssertionError(description)
        results.append(description)
    required = ['Mostik.sln', 'AGENTS.md', 'README.md', 'START_HERE_RU.md',
                'src/Mostik.Android/Mostik.Android.csproj', 'src/Mostik.Core/Mostik.Core.csproj',
                'tests/Mostik.Core.Smoke/Mostik.Core.Smoke.csproj',
                'docs/tasks/GOAL-001-FIRST-DEVICE/TASK.md', 'docs/PRIVACY.md',
                'docs/THIRD_PARTY.md', 'docs/BUILD_WINDOWS.md', 'docs/TEST_PLAN_REDMI.md',
                'docs/ROADMAP.md', 'scripts/Get-BuildAssets.ps1', 'scripts/Build.ps1']
    check(all((ROOT/p).is_file() for p in required), 'Присутствуют обязательные проекты, скрипты и документы')
    files = [p for p in ROOT.rglob('*') if p.is_file() and is_source_file(p)]
    for path in files:
        if path.suffix in {'.xml', '.csproj', '.props'} or path.name == 'NuGet.Config':
            ET.parse(path)
    results.append('XML проектов, ресурсов, Manifest и NuGet.Config разбирается')
    for path in files:
        if path.suffix == '.json':
            json.loads(path.read_text(encoding='utf-8-sig'))
    results.append('JSON конфигураций разбирается')
    app = ET.parse(ROOT/'src/Mostik.Android/Mostik.Android.csproj').getroot()
    check(app.findtext('./PropertyGroup/TargetFramework') == 'net10.0-android', 'Target framework: net10.0-android')
    check(app.findtext('./PropertyGroup/RuntimeIdentifier') == 'android-arm64', 'Сборка ограничена согласованным ARM64 ABI')
    check(app.findtext('./PropertyGroup/EmbedAssembliesIntoApk') == 'true', 'Assemblies включаются в самостоятельный debug APK')
    for project in ROOT.rglob('*.csproj'):
        tree = ET.parse(project).getroot()
        for element in tree.findall('.//ProjectReference') + tree.findall('.//AndroidAsset'):
            check((project.parent/element.attrib['Include']).is_file(), f"Существующая ссылка: {element.attrib['Include']}")
    models = json.loads((ROOT/'data/models.json').read_text(encoding='utf-8-sig'))
    check({m['Language'] for m in models} == {'ru', 'pl'} and len(models) == 2, 'Каталог содержит ровно RU и PL')
    for model in models:
        lang = model['Language']
        check(model['Id'] == f'vosk-model-small-{lang}-0.22', f'Закреплена версия модели {lang}')
        check(model['Url'] == 'https://alphacephei.com/vosk/models/' + model['Id'] + '.zip', f'Официальный HTTPS URL {lang}')
        check(re.fullmatch('[a-f0-9]{64}', model['Sha256']) is not None, f'Формат SHA-256 модели {lang}; это НЕ сверка скачанного ZIP')
    check(sum(m['DownloadBytes'] for m in models) == 99216122, 'Арифметика размеров опубликованных пакетов согласована')
    manifest = ET.parse(ROOT/'src/Mostik.Android/Properties/AndroidManifest.xml').getroot()
    ns = '{http://schemas.android.com/apk/res/android}'
    permissions = {e.attrib[ns+'name'] for e in manifest.findall('uses-permission')}
    check(permissions == {'android.permission.RECORD_AUDIO', 'android.permission.INTERNET', 'android.permission.ACCESS_NETWORK_STATE'}, 'Нет лишних разрешений Manifest')
    application = manifest.find('application')
    check(application is not None and application.attrib.get(ns+'allowBackup') == 'false', 'Backup отключён')
    check(application.attrib.get(ns+'usesCleartextTraffic') == 'false', 'Открытый HTTP отключён')
    translator = (ROOT/'src/Mostik.Android/Services/OfflineTranslator.cs').read_text(encoding='utf-8-sig')
    method = translator.split('public async Task<string> TranslateAsync(',1)[1].split('public void Dispose()',1)[0]
    method = re.sub(r'//[^\n]*', '', method)
    check('DownloadModelIfNeeded(' not in method, 'В теле TranslateAsync нет вызова загрузки моделей')
    code = '\n'.join(p.read_text(encoding='utf-8-sig') for p in ROOT.rglob('*.cs') if is_source_file(p))
    check('SpeechRecognizer' not in code and 'EXTRA_PREFER_OFFLINE' not in code, 'Нет подмены Vosk системным preferred-offline ASR')
    check('NotImplementedException' not in code, 'Нет NotImplementedException-заглушек')
    check(not any(p.suffix.lower() in {'.keystore','.jks','.pfx','.apk','.aab','.so'} for p in files), 'В исходной поставке нет ключей подписи или непроверенных бинарников')
    for path in ROOT.rglob('*.ps1'):
        check(path.read_bytes().startswith(b'\xef\xbb\xbf'), f'PowerShell UTF-8 BOM для Windows 5.1: {path.name}')
    sln=(ROOT/'Mostik.sln').read_text(encoding='utf-8-sig')
    for _, relative in re.findall(r'= "([^"]+)", "([^"]+\.csproj)"', sln):
        check((ROOT/relative.replace('\\','/')).is_file(), f'Существующий проект решения: {relative}')
    return results

def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--json', type=Path, help='Путь отчёта вне каталога исходников или в artifacts')
    args = parser.parse_args()
    try:
        results = validate()
    except (AssertionError, ValueError, OSError, ET.ParseError) as error:
        print('STRUCTURE_FAIL:', error, file=sys.stderr)
        return 1
    report = {'status': 'STRUCTURE_PASS_ONLY', 'checks': results,
              'not_run': ['C# compilation', 'Core.Smoke execution', 'PowerShell execution',
                          'native downloads/linking', 'Android runtime', 'Redmi offline/latency tests']}
    print('\n'.join('OK: '+result for result in results))
    print(f'STRUCTURE_PASS_ONLY: {len(results)} checks; C#/APK/телефон НЕ проверялись.')
    if args.json:
        args.json.parent.mkdir(parents=True, exist_ok=True)
        args.json.write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    return 0

if __name__ == '__main__':
    raise SystemExit(main())
