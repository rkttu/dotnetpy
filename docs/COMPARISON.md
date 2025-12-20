# C# → Python 통합 라이브러리 비교

이 문서는 C#에서 Python 코드를 실행하는 방향에 초점을 맞춘 비교입니다.

## 한눈에 보는 비교표

| 특성 | DotNetPy | pythonnet | IronPython | CSnakes |
|------|----------|-----------|------------|---------|
| **Python 버전** | 3.8+ (CPython) | 3.6+ (CPython) | 3.4 (자체 구현) | 3.9-3.13 (CPython) |
| **.NET 버전** | .NET 8+ | .NET 6+ | .NET 6+ | .NET 8-9 |
| **Native AOT** | ? 완벽 지원 | ? 미지원 | ? 미지원 | ? 미지원 |
| **GIL 관리** | 자동 (내부) | 수동 (`Py.GIL()`) | 없음 (자체 런타임) | 자동 (내부) |
| **Source Generator** | ? 불필요 | ? 불필요 | ? 불필요 | ? 필수 |
| **NumPy/Pandas** | ? 지원 | ? 지원 | ? 미지원 | ? Zero-copy 지원 |
| **양방향 통합** | ? C#→Py만 | ? 양방향 | ? 양방향 | ? C#→Py만 |
| **설정 복잡도** | ? 매우 쉬움 | ??? 중간 | ?? 쉬움 | ???? 복잡 |
| **데이터 교환** | JSON 기반 | 직접 객체 | 직접 객체 | 직접 객체 + Zero-copy |
| **성숙도** | ?? 실험적 | ? 안정 (15년+) | ? 안정 | ?? 신규 (2024) |

---

## 1. DotNetPy

### 특징
- **Zero Boilerplate**: GIL 관리, Source Generator 설정 불필요
- **Native AOT 완벽 지원**: PublishAot=true로 네이티브 바이너리 생성
- **자동 Python 탐색**: 시스템, PATH, uv 환경 자동 발견
- **JSON 기반 마샬링**: 단순하지만 복잡한 객체는 제한적

### 코드 예시
```csharp
using DotNetPy;

Python.Initialize();  // 자동 Python 탐색
var executor = Python.GetInstance();

// 단순 평가
var result = executor.Evaluate("1 + 2 + 3")?.GetInt32();

// 데이터 전달 및 캡처
var numbers = new[] { 1, 2, 3, 4, 5 };
using var stats = executor.ExecuteAndCapture(@"
    result = {'sum': sum(numbers), 'mean': sum(numbers)/len(numbers)}
", new Dictionary<string, object?> { { "numbers", numbers } });

Console.WriteLine(stats?.GetDouble("mean"));  // 3.0
```

### 장점
- 가장 빠른 시작 (5분 이내)
- Native AOT 배포 가능
- 최소한의 학습 곡선

### 단점
- Python 객체 직접 조작 불가
- 양방향 통합 미지원
- 실험적 상태

### 적합한 사용 사례
- 스크립팅/자동화
- Native AOT 앱에서 Python 호출
- 간단한 데이터 처리

---

## 2. pythonnet (Python.NET)

### 특징
- **양방향 통합**: C# ↔ Python 양방향 호출 지원
- **15년 이상의 역사**: 안정적이고 검증됨
- **직접 객체 조작**: `dynamic` 키워드로 Python 객체 직접 사용
- **수동 GIL 관리 필요**: `using (Py.GIL())` 패턴 필수

### 코드 예시
```csharp
using Python.Runtime;

// GIL 수동 관리 필요
using (Py.GIL())
{
    dynamic np = Py.Import("numpy");
    dynamic arr = np.array(new[] { 1, 2, 3, 4, 5 });
    Console.WriteLine(np.sum(arr));  // 15
    
    dynamic pd = Py.Import("pandas");
    dynamic df = pd.DataFrame(new Dictionary<string, object> {
        { "col1", new[] { 1, 2, 3 } },
        { "col2", new[] { 4, 5, 6 } }
    });
    Console.WriteLine(df.describe());
}
```

### Python에서 .NET 호출 (역방향)
```python
import clr
clr.AddReference("System.Windows.Forms")
from System.Windows.Forms import Form, Application

form = Form()
form.Text = "Hello from Python!"
Application.Run(form)
```

### 장점
- 양방향 통합 (Python ↔ .NET)
- Python 객체 직접 조작 (`dynamic`)
- 성숙하고 안정적
- 풍부한 문서와 커뮤니티

### 단점
- GIL 수동 관리 필요
- Native AOT 미지원
- 설정 복잡도 중간
- `PYTHONNET_PYDLL` 환경변수 설정 필요

### 적합한 사용 사례
- Python에서 .NET 라이브러리 사용
- 기존 Python 코드베이스와 깊은 통합
- Python 객체의 직접 조작이 필요한 경우

---

## 3. IronPython

### 특징
- **.NET에서 Python 재구현**: CPython이 아닌 자체 Python 런타임
- **GIL 없음**: .NET 스레드 모델 사용
- **완전한 .NET 통합**: Python 코드가 MSIL로 컴파일
- **Python 3.4 수준**: 최신 Python 기능 미지원

### 코드 예시
```csharp
using IronPython.Hosting;
using Microsoft.Scripting.Hosting;

ScriptEngine engine = Python.CreateEngine();
ScriptScope scope = engine.CreateScope();

// 변수 설정
scope.SetVariable("x", 10);
scope.SetVariable("y", 20);

// 실행
engine.Execute("result = x + y", scope);
int result = scope.GetVariable<int>("result");
Console.WriteLine(result);  // 30
```

### 장점
- GIL 없음 → 멀티스레딩 이점
- .NET 네이티브 통합
- 별도 Python 설치 불필요

### 단점
- **NumPy/Pandas 사용 불가** (C 확장 미지원)
- Python 3.4 수준 (3.x 완전 지원 아직 미완성)
- CPython 생태계와 호환성 문제
- 개발 속도 느림

### 적합한 사용 사례
- 순수 Python 스크립팅 (.NET 라이브러리와 함께)
- 멀티스레딩이 중요한 경우
- C 확장이 필요 없는 간단한 스크립트

---

## 4. CSnakes

### 특징
- **Source Generator**: Python 파일에서 C# 인터페이스 자동 생성
- **타입 힌트 기반**: Python 타입 힌트로 강타입 C# 메서드 생성
- **Zero-copy 버퍼**: NumPy 배열 메모리 직접 공유
- **GIL 자동 관리**: 내부 재귀 락 구현
- **Microsoft 후원**: Anthony Shaw (Python 팀) 주도

### 코드 예시

**Python 파일 (`example.py`)**:
```python
def calculate_stats(numbers: list[int]) -> dict[str, float]:
    import statistics
    return {
        "mean": statistics.mean(numbers),
        "stdev": statistics.stdev(numbers)
    }
```

**C# 코드**:
```csharp
// Source Generator가 자동 생성한 클래스 사용
using var env = Python.CreateEnvironment();
var example = env.Example();  // example.py에서 생성된 클래스

var stats = example.CalculateStats(new[] { 1, 2, 3, 4, 5 });
Console.WriteLine(stats["mean"]);  // 3.0
```

### 장점
- 타입 안전한 Python 호출
- NumPy Zero-copy 지원
- GIL 자동 관리
- OpenTelemetry/로깅 통합
- 가상환경/Conda 지원

### 단점
- Source Generator 설정 필요
- Python 파일에 타입 힌트 필수
- Native AOT 미지원
- 비교적 신규 프로젝트

### 적합한 사용 사례
- AI/ML 파이프라인
- 대용량 NumPy 데이터 처리
- 타입 안전성이 중요한 프로젝트
- .NET Aspire 통합

---

## 사용 사례별 권장 라이브러리

| 사용 사례 | 권장 | 이유 |
|-----------|------|------|
| **빠른 프로토타이핑** | DotNetPy | Zero boilerplate |
| **Native AOT 앱** | DotNetPy | 유일한 AOT 지원 |
| **AI/ML 파이프라인** | CSnakes | Zero-copy NumPy |
| **Python ↔ .NET 양방향** | pythonnet | 양방향 통합 |
| **순수 스크립팅** | IronPython | 별도 Python 불필요 |
| **기업/프로덕션** | pythonnet | 성숙하고 안정적 |
| **타입 안전성** | CSnakes | Source Generator |

---

## 성능 비교 (개념적)

```
초기화 속도:    IronPython > DotNetPy ? pythonnet > CSnakes
실행 속도:      CSnakes ≥ pythonnet ? DotNetPy > IronPython*
메모리 효율:    CSnakes (zero-copy) > pythonnet ? DotNetPy > IronPython
설정 시간:      DotNetPy < IronPython < pythonnet < CSnakes

* IronPython은 C 확장 미지원으로 NumPy 등 사용 불가
```

---

## 결론

### DotNetPy를 선택하세요:
- 가장 빠르게 Python을 .NET에서 실행하고 싶을 때
- Native AOT가 필요할 때
- 복잡한 설정 없이 시작하고 싶을 때

### pythonnet을 선택하세요:
- Python에서 .NET을 호출해야 할 때
- 성숙하고 검증된 솔루션이 필요할 때
- Python 객체를 직접 조작해야 할 때

### IronPython을 선택하세요:
- 별도 Python 설치 없이 스크립팅이 필요할 때
- GIL 없는 멀티스레딩이 필요할 때
- NumPy/Pandas가 필요 없을 때

### CSnakes를 선택하세요:
- AI/ML 워크로드를 .NET에 통합할 때
- NumPy 대용량 데이터의 Zero-copy가 중요할 때
- 타입 안전한 Python 호출이 필요할 때
