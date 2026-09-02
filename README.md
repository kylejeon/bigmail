# JLK-CTP 메일 발송기

병원 연락처 엑셀을 진료과 세그먼트로 분류해, 병원명·성함·진료과가 각각 반영된 제목과 본문을
자동 생성하고 Google Workspace SMTP로 속도를 조절해 발송하는 Windows 데스크톱 앱.

설계 문서: <https://claude.ai/code/artifact/9022c4f0-0a05-4cee-897d-3f80ef7107fb>
아래 §번호는 모두 그 문서의 절 번호입니다.

## 구성

```
src/
  JlkMailer.Core            net8.0          도메인 · 세그먼트 분류 · 토큰 치환 · 스로틀 정책 (외부 의존 0)
  JlkMailer.Infrastructure  net8.0          엑셀 · HTML 변환 · SMTP · SQLite · DPAPI
  JlkMailer.Application     net8.0          불러오기 · 중복제거 · 렌더링 · 발송 오케스트레이션
  JlkMailer.Presentation    net8.0          ViewModel (WPF 참조 없음 → Windows 없이 테스트됨)
  JlkMailer.App             net8.0-windows  WPF XAML · 코드비하인드만
  JlkMailer.Cli             net8.0          UI 없이 돌리는 경로 (§13 M0~M3)
tests/
  JlkMailer.Tests           net8.0          127개 테스트. 실제 납품 엑셀·HTML로 검증
```

핵심 규칙 두 가지:

- **Core는 외부 라이브러리를 참조하지 않는다.** 세그먼트 분류와 토큰 치환은 순수 함수이며,
  이 앱에서 실수가 가장 잦을 지점이라 단위 테스트로 고정한다.
- **ViewModel은 WPF를 참조하지 않는다.** 화면 로직이 `net8.0`이라 Windows 없이도 컴파일·테스트된다.
  `JlkMailer.App`에는 XAML과 코드비하인드만 남는다.

## 빌드

Windows:

```cmd
build.cmd
```
→ `publish\app\JlkMailer.exe` (단일 exe, .NET 런타임 설치 불필요), `publish\cli\jlkmail.exe`

macOS / Linux (WPF 제외, 로직 전체 빌드·테스트):

```bash
dotnet test JlkMailer.NoWpf.slnf
```

## CLI

```bash
jlkmail analyze                     # 엑셀 실측 통계 · 세그먼트 분포 (§03, §07)
jlkmail build --out out             # HTML 변환 결과를 세그먼트별 파일로 (§09 검증용)
jlkmail preview --row 2             # 특정 행의 메일 미리보기
jlkmail send --user a@b.com --dry-run
jlkmail export --out 발송결과.xlsx
```

앱 비밀번호는 인자로 받지 않습니다. 환경변수 `JLKMAIL_SECRET`을 사용하세요.

## 실행 전 확인 (§02 · §12)

1. **앱 비밀번호 발급 가능 여부** — 조직 정책으로 차단돼 있으면 OAuth 2.0 구현이 필요합니다
   (`SmtpOptions.AuthMode = OAuth2` 경로는 이미 있습니다. GCP 프로젝트·동의 화면 구성만 추가하면 됩니다).
2. **SPF / DKIM / DMARC** — `dig TXT jlkgroup.com`, `dig TXT _dmarc.jlkgroup.com`
3. **정보통신망법 제50조** — `(광고)` 표기와 사전동의 요건에 대한 사내 법무 확인.
   앱은 어느 결론이든 대응되도록 토글·수신거부 목록·야간 발송 차단을 갖추고 있으나,
   **준수를 보장하지는 않습니다.**
4. **테스트 발송** — Gmail·Outlook·네이버·모바일 4곳 실수신 확인. 앱이 이걸 마치기 전에는
   본 발송 버튼을 열지 않습니다.

## 안전장치

| 장치 | 위치 | 하는 일 |
|---|---|---|
| `UNIQUE(campaign_id, email_norm)` | send_log | 중복 발송 원천 차단. 재실행해도 이미 나간 주소는 큐에 안 들어감 |
| 연속 실패 차단기 | `SendOrchestrator` | 10건 연속 실패 시 자동 중단 (계정 잠긴 채 1,000건 실패 방지) |
| 야간 금지 | `ThrottlePolicy` | 21:00–08:00은 설정과 무관하게 항상 차단 |
| 재개 | `ResetStuckSending` | 앱 시작 시 `Sending` → `Queued` 복원 |
| 하드바운스 자동 제외 | `suppressions` | 550 응답 주소는 다음 캠페인에서 자동 제외 |
| DPAPI | `SecretStore` | 자격증명은 CurrentUser 범위 암호화. 평문 저장 없음 |

## 라이선스 주의

`SixLabors.ImageSharp`는 **2.1.13(Apache-2.0)** 으로 고정되어 있습니다.
3.x부터 Six Labors Split License(상용 시 유료)로 바뀌었습니다.
버전을 올릴 때는 `nuspec`의 `license`가 여전히 Apache-2.0인지 확인하세요.
