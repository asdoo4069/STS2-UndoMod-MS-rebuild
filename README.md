# sts2-mod-template

Slay the Spire 2 모드 개발을 위한 베이스 템플릿.

## 시작하기

### 1. 레포 클론

```bash
git clone https://github.com/asdoo4069/sts2-mod-template.git 모드이름
cd 모드이름
```

### 2. 빌드 전 필수 설정

`MinsuMod.csproj`에서 경로 수정:

```xml
<Sts2GamePath>D:\Games\Steam\steamapps\common\Slay the Spire 2</Sts2GamePath> <!-- 게임 경로 -->
<GodotPath>D:\WorkSpace\Megadot\MegaDot_v4.5.1-stable_mono_win64.exe</GodotPath> <!-- Megadot 경로 (.pck 빌드 시 필요) -->
```

### 3. 빌드

Megadot 또는 dotnet CLI로 빌드:

```bash
dotnet build
```

빌드 성공 시 `$(Sts2GamePath)\mods\모드이름\` 폴더에 `.dll`과 매니페스트 `.json`이 자동으로 복사된다.

`.pck`도 필요한 경우 `dotnet publish` 사용. Megadot이 헤드리스로 자동 실행되어 `.pck`까지 생성된다.

---

## 포크/새 모드 시작 시 이름 변경 체크리스트

아래 항목을 순서대로 수정한다.

- [ ] `MinsuMod.csproj` → `<AssemblyName>` 수정
- [ ] `MinsuMod.csproj` → `<Sts2GamePath>` 내 PC 경로로 수정 (필요한 경우)
- [ ] `MinsuMod.csproj` → `<GodotPath>` 내 PC 경로로 수정 (필요한 경우)
- [ ] `MinsuMod.json` → 파일명을 모드 이름으로 변경, `id`, `name`, `author` 수정
- [ ] `project.godot` → `config/name`, `project/assembly_name` 수정
- [ ] `export_presets.cfg` → `exclude_filter` 내 매니페스트 파일명 수정
- [ ] `MinsuMod.cs` → `ModId` 수정

> 수정하지 않아도 되는 것: `.csproj` 파일 이름, localization 폴더 이름

> localization 파일이 필요한 경우, 반드시 `모드이름/localization/언어/파일.json` 구조로 만들 것 (루트에 두면 게임 원본 localization과 충돌)

---

## 파일 구조

```
.
├── MinsuMod.cs           # 모드 진입점
├── MinsuMod.csproj       # 빌드 설정
├── MinsuMod.json         # 모드 매니페스트
├── MinsuMod.sln
├── project.godot         # Godot 프로젝트 설정 (.pck 빌드용)
└── export_presets.cfg    # Godot 내보내기 설정 (.pck 빌드용)
```
