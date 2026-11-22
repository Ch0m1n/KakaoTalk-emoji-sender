# KakaoTalk emoji sender · 아지트콘

Windows PC 카카오톡 옆에서 디시콘·아카콘을 검색하고 첨부하는 도우미예요. 카카오톡 클라이언트를 수정하거나 공식 이모티콘으로 등록하지 않아요.

## 설치

[최신 릴리스](https://github.com/Ch0m1n/KakaoTalk-emoji-sender/releases/latest)의 **AgitCon-win-x64.zip** 전체를 쓰기 가능한 폴더에 압축 해제하고 `AgitCon.exe`를 실행하세요. EXE만 따로 꺼내지 마세요. Windows 10/11 x64 및 .NET Framework 4.8이 필요해요. 이미지 변환용 Python은 동봉돼요.

## 기능

- 디시콘·아카콘 팩 검색/추가, 로컬 폴더, 파일명·별칭 검색
- 팩별 썸네일 그리드, 로컬 캐시, 미리 불러오기, 캐시 정리
- 카톡 입력창을 따라가는 플로팅 창, 위치 저장, UI 배율 설정
- 정지 이미지 미리보기 첨부, GIF 파일 첨부, 원본 두 개를 고르는 더블콘
- 정지 이미지 9분할, 순서·반복 조각을 지정하는 롱콘
- 선택 설치하는 로컬 의미 검색 및 이미지 분석, 현재 포커스된 작성 문장 추천
- **설정 → 업데이트 → 업데이트 확인**에서 릴리스 조회·다운로드·교체·재시작

일반 첨부는 전송 전 확인을 기본으로 해요. **움짤 바로 전송**을 켜거나 **롱콘 연속 전송**을 직접 시작하면 자동 전송 요청이 발생해요. 먼저 ‘나와의 채팅’에서 시험하세요. 대상이나 포커스를 확인하지 못하면 중단해요. 카톡 버전·배율·테마에 따라 호환성 차이가 있을 수 있어요.

## 데이터와 업데이트

설정·팩·별칭·캐시는 실행 폴더의 `data`, 선택 모델과 실행 환경은 `semantic` 등에 생겨요. 개인 데이터와 설치된 모델은 공개 소스 및 릴리스에 포함하지 않아요. 모델 추론은 PC 안에서 처리해요. 팩/이미지/선택 모델 다운로드와 사용자가 누른 업데이트 확인에는 인터넷을 사용해요. 전역 키 입력이나 채팅 기록은 수집하지 않아요.

업데이트는 정식 GitHub Release를 확인해요. 소스 커밋만으로는 표시되지 않아요. 사용자가 업데이트를 누르면 GitHub SHA-256·버전·ZIP 경로를 검증하고 앱을 교체해요. 기존 `data`와 모델은 덮어쓰지 않아요. 실패하면 복구를 시도하고 이전 파일은 실행 폴더의 `.updates-backup`에 보관해요. 다운로드는 `%LOCALAPPDATA%/AgitCon/updates`에 보관해요.

## 빌드

Windows x64, PowerShell 7, Python 3.11 x64, .NET Framework 4.8 개발 도구, Windows 10/11 SDK가 필요해요.

```powershell
python tools/prepare-build.py
pwsh -NoProfile -File build.ps1
pwsh -NoProfile -Command "& ./tools/test-build.ps1 -Flags @('--app-update-test','--longcon-test','--ui-zoom-test','--release-self-check','--copy-refresh-test')"
python tools/package-release.py
```

빌드는 `artifacts/build`, 시험은 `artifacts/tests`, 배포 ZIP은 `releases/<버전>`에 출력돼요. `dist`는 로컬 실행용으로 분리돼요. AI 모델은 빌드에 필요하지 않아요. [릴리스 절차](docs/RELEASE.md)를 참고하세요.

## 검증 범위

업데이트 검증·ZIP 보호·파일 복구·데이터 보존과 기존 기능의 격리 테스트를 제공해요. 테스트 통과가 모든 카톡 버전·다른 PC에서의 실제 전송 성공을 보장하지는 않아요. 특히 롱콘 자동 연속 전송은 실제 카톡 검증이 아직 필요해요. 제보에는 개인 대화/전체 데이터 폴더 대신 재현 순서·버전·배율과 개인정보를 지운 진단을 첨부하세요.

## 외부 구성요소

카카오·디시인사이드·아카라이브의 공식 앱이 아니에요. 콘의 권리는 각 제작자에게 있어요. 사용자 콘 이미지는 배포하지 않아요. WebView2, Python, Pillow, imageio-ffmpeg 및 FFmpeg 라이선스·빌드 정보는 ZIP의 `licenses`와 [의미 검색 구성요소 안내](semantic/THIRD_PARTY.md)에 포함돼요.
