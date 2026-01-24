// Assets/Tools/UrpFbxAutoMaterial/Editor/LogUtil.cs
#nullable enable
using UnityEngine;

namespace UrpFbxAutoMaterial
{
    /// <summary>
    /// ログレベル設定
    /// </summary>
    public enum LogLevel
    {
        /// <summary>エラーと警告のみ</summary>
        Minimal = 0,
        /// <summary>通常のログ（デフォルト）</summary>
        Normal = 1,
        /// <summary>詳細なデバッグログ</summary>
        Verbose = 2
    }

    public static class LogUtil
    {
        private const string Prefix = "[URP FBX AutoMat] ";

        /// <summary>
        /// 現在のログレベル。デフォルトは Normal。
        /// Verbose にすると詳細なデバッグ情報が出力されます。
        /// </summary>
        public static LogLevel CurrentLevel = LogLevel.Normal;

        /// <summary>通常の情報ログ（Normal 以上で出力）</summary>
        public static void Info(string msg)
        {
            if (CurrentLevel >= LogLevel.Normal)
                Debug.Log(Prefix + msg);
        }

        /// <summary>詳細なデバッグログ（Verbose のみで出力）</summary>
        public static void Verbose(string msg)
        {
            if (CurrentLevel >= LogLevel.Verbose)
                Debug.Log(Prefix + "[DEBUG] " + msg);
        }

        /// <summary>警告ログ（常に出力）</summary>
        public static void Warn(string msg) => Debug.LogWarning(Prefix + msg);

        /// <summary>エラーログ（常に出力）</summary>
        public static void Error(string msg) => Debug.LogError(Prefix + msg);

        /// <summary>例外ログ（常に出力）</summary>
        public static void Exception(System.Exception ex) => Debug.LogException(ex);
    }
}
