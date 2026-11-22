# 릴리스

1. `version.json`의 version을 이전보다 높은 `major.minor.patch`로 바꿔요. repository 값은 유지해요.
2. 변경과 테스트 결과를 커밋하고 main에 push해요.
3. 같은 버전의 태그 `v1.0.1` 등을 push하면 Windows CI가 빌드·격리 테스트·패키징 후 GitHub Release에 ZIP을 올려요.
4. 릴리스 완료 후 앱의 설정 → 업데이트 → 업데이트 확인에서 찾아요.

```powershell
git add <변경 파일>
git commit -m "Describe the change"
git push origin main
git tag v1.0.1
git push origin v1.0.1
```

배포 파일 이름 `AgitCon-win-x64.zip`은 유지해야 해요. GitHub가 제공하는 asset digest로 다운로드를 검증해요. 공개 저장소에는 설정/콘 이미지/로그/개인 경로/모델을 올리지 마세요. 카톡 실제 전송을 CI에서 실행하지 않아요.
