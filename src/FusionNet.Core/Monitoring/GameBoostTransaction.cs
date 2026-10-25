namespace FusionNet.Core.Monitoring;

public interface IGameBoostTransactionPlatform
{
    void RequireAdministrator();
    void ValidateTargets();
    void SaveSnapshot();
    void InstallRecovery();
    void AddRule(int index);
    IReadOnlyList<string> GetOwnedRules();
    void RemoveRule(string name);
    void RemoveRecovery();
    void SaveStatus(GameBoostStatus status);
}

public static class GameBoostTransaction
{
    public static GameBoostStatus Start(IGameBoostTransactionPlatform platform, int targetCount)
    {
        platform.RequireAdministrator();
        platform.ValidateTargets();
        platform.SaveSnapshot();
        platform.InstallRecovery();
        try
        {
            platform.ValidateTargets();
            for (var index = 0; index < targetCount; index++) platform.AddRule(index);
            if (platform.GetOwnedRules().Count != targetCount) throw new InvalidOperationException("Rule verification failed.");
            var status = new GameBoostStatus("Active", targetCount, "قواعد الحظر الصادر موجودة. هذا حظر اتصال كامل لكل نسخ EXE، وليس تقييد سرعة.", DateTimeOffset.UtcNow);
            platform.SaveStatus(status);
            return status;
        }
        catch { return Restore(platform); }
    }

    public static GameBoostStatus Restore(IGameBoostTransactionPlatform platform)
    {
        try
        {
            foreach (var name in platform.GetOwnedRules()) platform.RemoveRule(name);
            if (platform.GetOwnedRules().Count != 0) throw new InvalidOperationException("Rule absence was not verified.");
            var status = new GameBoostStatus("Restored", 0, "تم التحقق من زوال قواعد الجلسة. تُنظّف مهمة الاسترجاع بعد حفظ هذه النتيجة؛ قد تعيد المحاولة إذا تعذر تنظيفها.");
            platform.SaveStatus(status);
            platform.RemoveRecovery();
            return status;
        }
        catch
        {
            var status = new GameBoostStatus("Pending", -1, "لم يُتحقق من الاسترجاع؛ تبقى المهمة المستقلة للمحاولة مجددًا.");
            try { platform.SaveStatus(status); } catch { /* durable recovery task remains authoritative */ }
            return status;
        }
    }
}
