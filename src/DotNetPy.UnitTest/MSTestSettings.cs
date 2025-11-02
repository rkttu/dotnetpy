// 모든 테스트를 순차적으로 실행
// SequentialTestRunner가 단일 테스트 메서드로 모든 테스트를 실행합니다
[assembly: Parallelize(Workers = 1, Scope = ExecutionScope.MethodLevel)]
[assembly: DoNotParallelize]
