using System;

namespace ProjectC.Client
{
    internal sealed class SecureSessionStoreException : Exception
    {
        public int Status { get; }
        public SecureSessionStoreException(string store, string operation, int status)
            : base($"{store}에서 로그인 정보를 {operation}할 수 없습니다. (상태 {status}) 보안 저장소의 잠금 및 접근 권한을 확인하세요. 기존 계정 정보는 보존됩니다.")
        {
            Status = status;
        }
    }
}
