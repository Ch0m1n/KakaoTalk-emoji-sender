# KakaoTalk emoji sender · 아지트콘

Windows PC 카카오톡 옆에서 디시콘·아카콘을 검색·첨부하고, 채팅방의 GIF·움직이는 WebP를 자동재생하는 통합 도우미예요. 설치된 카카오톡 파일을 수정하거나 공식 이모티콘으로 등록하지 않아요. 자동재생을 시작하면 Frida가 실행 중인 카톡의 이미지 표시 경로에 연결하며 종료 시 해제해요.

## 설치

[최신 릴리스](https://github.com/Ch0m1n/KakaoTalk-emoji-sender/releases/latest)에서 **AgitCon-Setup-버전-win-x64.exe** 또는 **win-x86.exe**를 받아 설치하세요. 일반적인 64비트 Windows는 x64를, 32비트 Windows는 x86을 사용해요. 앱과 카톡의 비트 수는 별개이며, x64 앱으로 기존 32비트 카톡을 사용할 수 있어요. Windows 10/11과 .NET Framework 4.8이 필요해요.

관리자 권한 없이 기본적으로 `%LOCALAPPDATA%/Programs/AgitCon`에 설치해요. 시작 메뉴, 선택형 바탕 화면/로그인 자동 실행 바로가기, 프로그램 제거를 제공해요. Python·Frida·이미지 변환기는 포함돼 있어 별도 설치가 필요 없어요.

예전 압축판에서 옮긴다면 설치 도중 **예전 압축판 폴더에서 콘과 설정 가져오기**를 선택하고 `AgitCon.exe`와 `data`가 있는 폴더를 지정하세요. 설정·별칭·받아둔 콘을 복사하고 내부 이미지 경로를 옮겨요. 원본과 이미 설치된 데이터는 덮어쓰지 않아요. 선택 AI 모델은 가져오지 않으므로 새 설치에서 다시 준비해야 해요. 설치판 업데이트와 제거 시 기존 데이터·모델은 유지돼요.

기존 KakaoAutoPlay는 먼저 완전히 종료하고, 별도로 등록했던 자동 시작도 그 도우미에서 해제하세요. 이후 통합 앱의 로그인 자동 실행과 **설정 → 움짤 자동재생 → 아지트콘 실행 시 자동재생 시작**을 사용하세요. 두 재생기를 동시에 실행하지 마세요. 설치 없이 쓰려면 **AgitCon-win-x64.zip / AgitCon-win-x86.zip 포터블** 전체를 쓰기 가능한 폴더에 풀고 `AgitCon.exe`를 실행하세요. EXE만 따로 꺼내지 마세요. 포터블은 설정·콘을 그 폴더에 보관하며 ZIP으로 업데이트해요.

설치 마법사는 95/98 시절의 클래식 스타일을 사용해요. 종이가 폴더 사이로 날아가는 애니메이션은 [Microsoft 공식 VCSamples의 원본 FILECOPY.AVI](https://github.com/microsoft/VCSamples/blob/9e1d4475555b76a17a3568369867f1d7b6cc6126/VC2008Samples/MFC/general/CmnCtrl1/filecopy.avi)를 변경 없이 재생해요. 파일 출처·SHA-256은 [source.json](installer/assets/source.json), 저작권·라이선스는 [Microsoft VCSamples 안내](third_party/Microsoft-VCSamples-license.txt)에 보존해요. 설치 스타일과 별개로 지원 OS는 Windows 10/11이에요.

## 기능

- 디시콘·아카콘 팩 검색/추가, 로컬 폴더, 파일명·별칭 검색
- 팩별 썸네일 그리드, 로컬 캐시, 미리 불러오기, 캐시 정리. 움짤은 마우스를 올린 콘 하나만 재생하고 벗어나면 정지 썸네일로 돌아와요.
- 카톡 입력창을 따라가는 플로팅 창, 위치 저장, UI 배율 설정
- 정지 이미지 미리보기 첨부, GIF 파일 첨부, 원본 두 개를 고르는 더블콘
- 정지 이미지 9분할, 순서·반복 조각을 지정하는 롱콘
- 선택 설치하는 로컬 의미 검색 및 이미지 분석, 현재 포커스된 작성 문장 추천
- **설정 → 움짤 자동재생** 탭 안에서 시작·중지·상태·사진 호버 옵션·진단 ZIP을 바로 사용해요. 별도 재생기 창·트레이·업데이터는 없어요. 설정을 닫아도 재생은 유지하며 아지트콘 완전 종료 시 정리해요.
- **설정 → 업데이트 → 업데이트 확인**에서 릴리스 조회·다운로드·교체·재시작

아카콘 이름 검색이 사이트의 브라우저 확인/로그인에 막히면 앱 안에 검색 확인창을 열어요. 확인을 직접 마친 뒤 **검색 결과 가져오기**를 누르세요. 세션은 `data/arca-browser`에 로컬로 보관해요. 앱은 로그인 폼이나 쿠키를 추출하지 않고 검색 결과 카드만 읽어요. 사이트가 WebView2에서도 접속을 제한하면 팩 링크 또는 HTML 등록을 사용할 수 있어요.

썸네일 미리 불러오기는 압축된 PNG를 최대 32MB까지 메모리에 보관해요. 펼친 이미지 캐시는 16MB를 기준으로 오래된 항목부터 정리하되 화면에서 사용 중인 이미지는 보호해요. 숨긴 격자의 이미지 참조와 호버가 끝난 GIF의 프레임·스트림은 해제해요. 이 수치는 앱 전체 RAM 상한이 아니며 전송용 원본이나 디스크 캐시를 삭제하지 않아요.

일반 첨부는 전송 전 확인을 기본으로 해요. **움짤 바로 전송**을 켜거나 **롱콘 연속 전송**을 직접 시작하면 자동 전송 요청이 발생해요. 먼저 ‘나와의 채팅’에서 시험하세요. 신형 Qt 롱콘은 확인된 전송 팝업의 버튼만 호출하며 채팅 입력창에 Enter를 보내지 않아요. GIF는 전송 전 확인 설정을 읽지 못하면 파일 이름까지 지정하고 ‘열기’를 직접 누르도록 안내해요. 기존 ‘움짤 바로 전송’ 선택 시에는 자동 열기·확인창 처리 경로를 사용해요. 대상이나 포커스를 확인하지 못하면 중단해요. 카톡 버전·배율·테마에 따라 호환성 차이가 있을 수 있어요.

트레이의 **아지트콘 열기**는 일반 창으로 유지해요. **입력창 옆 플로팅으로 돌아가기**를 누르면 카톡과 함께 움직이며 다른 앱에서는 숨겨요. 플로팅의 창 위치 연결과 첨부 입력창 검증은 별개예요. 입력창을 식별하지 못해도 카톡 옆에 표시하며 자동 첨부는 확인된 입력창에만 요청해요.

## 데이터와 업데이트

설정·팩·별칭·캐시는 실행 폴더의 `data`, 선택 모델과 실행 환경은 `semantic` 등에 생겨요. 개인 데이터와 설치된 모델은 공개 소스 및 릴리스에 포함하지 않아요. 모델 추론은 PC 안에서 처리해요. 팩/이미지/선택 모델 다운로드와 사용자가 누른 업데이트 확인에는 인터넷을 사용해요. 전역 키 입력이나 채팅 기록은 수집하지 않아요.

설치판은 같은 비트 수의 새 설치 파일로 업데이트하고, 기존 압축판은 ZIP 업데이트를 사용해요. 업데이트는 정식 GitHub Release를 확인해요. 소스 커밋만으로는 표시되지 않아요. 사용자가 업데이트를 누르면 GitHub SHA-256·버전·ZIP 경로를 검증하고 앱을 교체해요. 기존 `data`와 모델은 덮어쓰지 않아요. ZIP 교체 실패 시 복구를 시도하고 이전 파일은 실행 폴더의 `.updates-backup`에 보관해요. 설치판은 설치 실패 시 오류를 알리며 설치 파일로 재시도할 수 있어요. 설치 실패 전체의 자동 롤백을 보장하지는 않아요. 다운로드는 `%LOCALAPPDATA%/AgitCon/updates`에 보관해요. 자동재생의 원본·프레임·진단은 실행 폴더의 `codecs/autoplay/artifacts/kakao-internals`, 사진 호버 옵션은 기존 `%LOCALAPPDATA%/KakaoAutoPlay/settings.json`을 사용해요.

## 빌드

Windows x64, PowerShell 7, Python 3.11 x64, .NET Framework 4.8 개발 도구, Windows 10/11 SDK가 필요해요.

```powershell
python tools/prepare-build.py
python tools/prepare-platform.py x64  # x86도 같은 절차
pwsh -NoProfile -File build.ps1 -Architecture x64 -OutputName AgitCon-x64.exe
pwsh -NoProfile -Command "& ./tools/test-build.ps1 -OutputName AgitCon-x64.exe -Flags @('--autoplay-integration-test','--app-update-test','--preview-paste-test','--longcon-test')"
python tools/package-release.py --architecture x64
python tools/test-unified.py x64
pwsh -NoProfile -File tools/prepare-installer.ps1
pwsh -NoProfile -File tools/build-installer.ps1 -Architecture x64
pwsh -NoProfile -File tools/test-installer.ps1 -Architecture x64 -UseUpdater
pwsh -NoProfile -File tools/test-installer-preview.ps1 -Architecture x64
```

빌드는 `artifacts/build`, 시험은 `artifacts/tests`, 배포 ZIP은 `releases/<버전>`에 출력돼요. `dist`는 로컬 실행용으로 분리돼요. AI 모델은 빌드에 필요하지 않아요. [릴리스 절차](docs/RELEASE.md)를 참고하세요.

## 자동재생 호환 범위

[자동재생 원본 v11.3.0](https://github.com/Ch0m1n/Kakao-Auto-Animated-image-play/tree/387eb04)과 [머지 PR #2](https://github.com/Ch0m1n/Kakao-Auto-Animated-image-play/pull/2)의 사진 축소/호버 기능을 가져왔어요. 머지 없이 닫힌 [성능 PR #1](https://github.com/Ch0m1n/Kakao-Auto-Animated-image-play/pull/1)의 DIB 캐시·잘림 매칭·작은 GIF 대체 디코딩도 충돌을 해결해 통합했어요. 세부 출처는 [UPSTREAM](autoplay/UPSTREAM.md)에 있어요.

| 카톡 종류 | 자동재생 엔진 | 콘 자동 첨부 |
| --- | --- | --- |
| 기존 32비트 KakaoTalk.exe | 기존 다운로드/재생 경로 | 기존 RichEdit 입력창 경로 |
| Qt 64비트 KakaoTalkUI.exe | Qt 사진/묶음 WebP 경로 | 26.10.0.492 프로세스 탐지와 접근성 입력창을 통한 정지 이미지 Ctrl+V 경로를 추가했어요. 실제 전송은 검증 전이며 선택 상태를 제공하지 않으면 복사만 해요. GIF 원본 첨부와 롱콘 연속 전송 경로를 추가했어요. 실제 26.10.0.492 전송은 검증 전이에요. 9분할·더블콘 파일 첨부는 Qt에서 아직 미지원이에요. |
| 기존 구조 64비트 KakaoTalk.exe | 저장된 원본 재생 시험 지원, 원본 자동 다운로드 미지원 | 클라이언트별 별도 확인 필요 |

설치 파일의 x86/x64 표시는 **앱·동봉 런타임의 아키텍처**이며 카톡 모든 버전의 지원 표시가 아니에요. 사진 축소/호버는 기존 32비트 카톡 일반 사진에 한정돼요. 전체 묶음 배치·상품 이모티콘·임의의 강한 잘림을 모두 처리하지는 않아요.

## 검증 범위

v2.0.0에서 x86/x64 앱·Python·Frida·Pillow·WebView2 로더의 실제 PE 아키텍처를 검사했어요. 패키지 런타임, 자체 GDI/Frida 시험의 각 아키텍처별 66개 크기·20개 잘림 조건과 64개 픽셀 조건, 설치→업데이트→제거 후 설정·모델 보존을 확인했어요. x86은 64비트 Windows의 WOW64에서 검사했으며 실제 32비트 OS와 친구 PC 4K 실사용은 확인하지 않았어요. 이번 통합 작업에서 실제 카톡에 자동 전송 시험을 하지는 않았어요.

업데이트 검증·ZIP 보호·파일 복구·데이터 보존과 기존 기능의 격리 테스트를 제공해요. v1.0.4에서는 사용자 PC의 실제 카톡에서 수동 Enter 없이 롱콘 4조각이 모두 자동 전송됐다는 확인을 받았고, 완료 로그도 확인했어요. 롱콘 시작 후에는 Enter·전송을 직접 누르지 말고 자동 처리를 기다려주세요. 이 결과가 모든 카톡 버전·다른 PC·배율에서의 성공을 보장하지는 않아요. 제보에는 개인 대화/전체 데이터 폴더 대신 재현 순서·버전·배율과 개인정보를 지운 진단을 첨부하세요.

## 외부 구성요소

카카오·디시인사이드·아카라이브의 공식 앱이 아니에요. 콘의 권리는 각 제작자에게 있어요. 사용자 콘 이미지는 배포하지 않아요. WebView2, Python, Frida, Pillow, imageio-ffmpeg 및 FFmpeg 라이선스·빌드 정보는 ZIP의 `licenses`와 [의미 검색 구성요소 안내](semantic/THIRD_PARTY.md)에 포함돼요.
