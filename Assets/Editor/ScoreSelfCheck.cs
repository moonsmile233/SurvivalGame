using System;
using Survival.Core;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 计分公式自检（M6 C6 / 测试计划 §6.2 的“纯逻辑”层）。Editor-only，不引测试包。
///
/// 入口：
/// - 菜单 `Survival/Score Self Check`（结果写 Console）；
/// - batchmode `-executeMethod ScoreSelfCheck.RunBatch`（失败抛异常 → 非 0 退出）。
///
/// 断言值来自 `docs/proposals/proposal-M6-内容-实施计划.md` §4.4 的参考分表。
/// 公式参数在 `Assets/Configs/cfg_GameConfig.asset`；若策划改分段/倍率，这里必须同步，
/// 否则失败信息会直接指出是哪一档、哪一输入对不上。
/// </summary>
public static class ScoreSelfCheck
{
    private const string ConfigPath = "Assets/Configs/cfg_GameConfig.asset";

    private static int s_failures;

    [MenuItem("Survival/Score Self Check")]
    public static void Run()
    {
        s_failures = 0;
        var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
        if (config == null)
        {
            Debug.LogError($"[ScoreSelfCheck] FAIL 找不到 {ConfigPath}");
            return;
        }

        CheckSegments(config);
        CheckDifficultyTable(config);
        CheckGranularityAndReset(config);

        if (s_failures == 0)
        {
            Debug.Log("[ScoreSelfCheck] PASS 分段/最终分/粒度/Reset 全部通过");
        }
        else
        {
            Debug.LogError($"[ScoreSelfCheck] FAIL {s_failures} 项不匹配（见上方逐条日志）");
        }
    }

    /// <summary>batchmode 入口：任何失败都抛出，让构建/CI 以非 0 退出。</summary>
    public static void RunBatch()
    {
        Run();
        if (s_failures > 0)
        {
            throw new InvalidOperationException($"[ScoreSelfCheck] {s_failures} checks failed");
        }
    }

    /// <summary>分段边界：0/15/30/45/60 的原始存活分。</summary>
    private static void CheckSegments(GameConfig config)
    {
        float[] seconds = { 0f, 15f, 30f, 45f, 60f };
        float[] expected = { 0f, 150f, 330f, 555f, 855f };
        var score = new ScoreService(config);

        for (int i = 0; i < seconds.Length; i++)
        {
            Expect($"SurvivalRawFor({seconds[i]}s)", score.SurvivalRawFor(seconds[i]), expected[i]);
        }

        Expect("SurvivalRawFor(-5s) 钳 0", score.SurvivalRawFor(-5f), 0f);
        Expect("SurvivalRawFor(999s) 钳 60s", score.SurvivalRawFor(999f), 855f);
    }

    /// <summary>§4.4 参考分表：三档 × 胜利/失败组合。</summary>
    private static void CheckDifficultyTable(GameConfig config)
    {
        CheckCase(config, Difficulty.Easy, 2f, 100, true, 320);
        CheckCase(config, Difficulty.Normal, 2f, 100, true, 356);
        CheckCase(config, Difficulty.Hard, 2f, 100, true, 392);

        CheckCase(config, Difficulty.Easy, 5f, 100, true, 350);
        CheckCase(config, Difficulty.Normal, 5f, 100, true, 395);
        CheckCase(config, Difficulty.Hard, 5f, 100, true, 440);

        CheckCase(config, Difficulty.Easy, 15f, 70, false, 220);
        CheckCase(config, Difficulty.Normal, 15f, 70, false, 286);
        CheckCase(config, Difficulty.Hard, 15f, 70, false, 352);

        CheckCase(config, Difficulty.Easy, 30f, 50, false, 380);
        CheckCase(config, Difficulty.Normal, 30f, 50, false, 494);
        CheckCase(config, Difficulty.Hard, 30f, 50, false, 608);

        CheckCase(config, Difficulty.Easy, 60f, 100, true, 1155);
        CheckCase(config, Difficulty.Normal, 60f, 100, true, 1442);
        CheckCase(config, Difficulty.Hard, 60f, 100, true, 1728);

        // 失败但撑满 60s：无胜利奖励。
        CheckCase(config, Difficulty.Normal, 60f, 0, false, 1112);

        // 血量钳到 PlayerMaxHealth（防御异常调用）。
        config.SetActiveDifficulty(Difficulty.Easy);
        var score = new ScoreService(config);
        score.Finish(15f, 999, false);
        Expect("Finish 血量钳到 MaxHealth", score.RemainingHealth, config.PlayerMaxHealth);
    }

    private static void CheckCase(GameConfig config, Difficulty difficulty, float elapsed, int health, bool won,
        int expected)
    {
        // 短局胜利（2s/5s）靠 E2E 的 roundDuration 覆盖把“回合时长”缩短；
        // Finish 对胜利固定取 RoundDuration，所以自检也要用同一覆盖，否则会按 60s 计分。
        config.SetPerfRoundDurationOverride(won ? elapsed : 0f);
        config.SetActiveDifficulty(difficulty);
        var score = new ScoreService(config);
        score.Finish(elapsed, health, won);
        config.SetPerfRoundDurationOverride(0f);
        Expect(
            $"FinalScore {difficulty} {elapsed:0}s hp={health} won={won}",
            score.FinalScore,
            expected);
    }

    /// <summary>0.1s 粒度：Advance 每帧重算、HUD 显示向下取整；Reset 干净。</summary>
    private static void CheckGranularityAndReset(GameConfig config)
    {
        config.SetActiveDifficulty(Difficulty.Easy); // 倍率 1.0，便于直接对照原始分
        var score = new ScoreService(config);

        score.Advance(7.3f);
        Expect("DisplayScore 7.3s", score.DisplayScore, 73);

        int events = 0;
        score.OnScoreChanged += _ => events++;

        score.Advance(7.35f); // +0.05s → 0.5 分：取整后仍 73，不应触发
        Expect("DisplayScore 7.35s 不变", score.DisplayScore, 73);
        Expect("未变化不触发事件", events, 0);

        score.Advance(7.4f); // 74.0 → 显示 74，触发一次
        Expect("DisplayScore 7.4s", score.DisplayScore, 74);
        Expect("变化触发一次事件", events, 1);

        // 纯函数：先 30s 再回 15s，不能残留累加值。
        score.Advance(30f);
        Expect("DisplayScore 30s", score.DisplayScore, 330);
        score.Advance(15f);
        Expect("DisplayScore 回退到 15s", score.DisplayScore, 150);

        score.Finish(30f, 50, false);
        Expect("Finish 后 SurvivalScore", score.SurvivalScore, 330);
        Expect("Finish 后 HealthScore", score.HealthScore, 50);
        Expect("失败无胜利奖励", score.WinBonus, 0);
        Expect("Kills 恒 0", score.Kills, 0);
        Expect("KillScore 恒 0", score.KillScore, 0);

        score.Reset();
        Expect("Reset DisplayScore", score.DisplayScore, 0);
        Expect("Reset FinalScore", score.FinalScore, 0);
        Expect("Reset RemainingHealth", score.RemainingHealth, 0);

        config.SetActiveDifficulty(Difficulty.Normal);
    }

    private static void Expect(string label, float actual, float expected)
    {
        if (Mathf.Abs(actual - expected) <= 0.0001f)
        {
            return;
        }

        s_failures++;
        Debug.LogError($"[ScoreSelfCheck] FAIL {label}: expected {expected}, got {actual}");
    }
}
