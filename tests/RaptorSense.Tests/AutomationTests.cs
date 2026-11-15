using RaptorSense.Automation;
using RaptorSense.Profiles;
using RaptorSense.Thermal;
using Xunit;

namespace RaptorSense.Tests;

public sealed class AutomationTests
{
    static AutomationState State(bool onBattery = false, params string[] apps) =>
        new(new HashSet<string>(apps, StringComparer.OrdinalIgnoreCase), onBattery);

    static AutomationRule ForApp(string app) => new() { Trigger = AutomationTrigger.AppOpen, App = app, Power = PowerProfile.Turbo };

    // ---- Which rules apply ----

    [Theory]
    [InlineData("game")]
    [InlineData("GAME")]
    [InlineData("game.exe")]
    [InlineData("  Game.EXE ")]
    public void App_names_match_however_they_were_typed(string typed)
    {
        Assert.True(ForApp(typed).IsActive(State(false, "game")));
    }

    [Fact]
    public void App_rule_needs_that_app_open()
    {
        Assert.False(ForApp("game").IsActive(State(false, "browser")));
    }

    [Fact]
    public void App_rule_with_no_name_never_applies()
    {
        Assert.False(ForApp("  ").IsActive(State(false, "game")));
    }

    [Fact]
    public void Disabled_rule_never_applies()
    {
        Assert.False((ForApp("game") with { Enabled = false }).IsActive(State(false, "game")));
    }

    [Theory]
    [InlineData(AutomationTrigger.OnBattery, true, true)]
    [InlineData(AutomationTrigger.OnBattery, false, false)]
    [InlineData(AutomationTrigger.PluggedIn, true, false)]
    [InlineData(AutomationTrigger.PluggedIn, false, true)]
    public void Power_source_rules_follow_the_power_source(AutomationTrigger trigger, bool onBattery, bool active)
    {
        Assert.Equal(active, new AutomationRule { Trigger = trigger }.IsActive(State(onBattery)));
    }

    [Fact]
    public void Sound_and_screen_rules_follow_what_they_watch()
    {
        var sound = new AutomationRule { Trigger = AutomationTrigger.SoundPlaying };
        var screen = new AutomationRule { Trigger = AutomationTrigger.ScreenOff };

        Assert.False(sound.IsActive(State()));
        Assert.False(screen.IsActive(State()));
        Assert.True(sound.IsActive(State() with { SoundPlaying = true }));
        Assert.True(screen.IsActive(State() with { ScreenOff = true }));
    }

    [Theory]
    [InlineData(null, 4.9, false)]   // five minutes unless the rule says otherwise
    [InlineData(null, 5.0, true)]
    [InlineData(2, 1.9, false)]
    [InlineData(2, 2.0, true)]
    [InlineData(0, 0.5, false)]      // a hand-edited 0 would otherwise switch the lights off for good
    [InlineData(0, 1.0, true)]
    [InlineData(100000, 240.0, true)]
    public void Idle_rule_waits_for_its_minutes(int? minutes, double idleMinutes, bool active)
    {
        var rule = new AutomationRule { Trigger = AutomationTrigger.Idle, IdleMinutes = minutes };

        Assert.Equal(active, rule.IsActive(State() with { Idle = TimeSpan.FromMinutes(idleMinutes) }));
    }

    // ---- Noticing sound ----

    static readonly TimeSpan Poll = TimeSpan.FromSeconds(0.5);

    static void Feed(SoundDetector detector, float peak, double seconds)
    {
        for (double t = 0; t < seconds; t += Poll.TotalSeconds) detector.Update(peak, Poll);
    }

    [Fact]
    public void Sound_counts_as_playing_after_a_few_seconds_of_it()
    {
        var detector = new SoundDetector();

        Feed(detector, 0.2f, 2.5);
        Assert.False(detector.Playing);

        Assert.True(detector.Update(0.2f, Poll));   // the change is reported once
        Assert.True(detector.Playing);
        Assert.False(detector.Update(0.2f, Poll));
    }

    [Fact]
    public void A_notification_chime_does_not_count()
    {
        var detector = new SoundDetector();

        for (int chime = 0; chime < 10; chime++)
        {
            Feed(detector, 0.5f, 1.5);
            Feed(detector, 0, 2);
        }

        Assert.False(detector.Playing);
    }

    [Fact]
    public void A_reading_that_lands_between_two_drum_hits_does_not_start_the_count_again()
    {
        var detector = new SoundDetector();

        for (int i = 0; i < 4 && !detector.Playing; i++)
        {
            Feed(detector, 0.3f, 1);
            detector.Update(0, Poll);
        }

        Assert.True(detector.Playing);
    }

    [Fact]
    public void The_gap_between_two_songs_does_not_end_it_but_a_long_silence_does()
    {
        var detector = new SoundDetector();
        Feed(detector, 0.2f, 5);

        Feed(detector, 0, 10);
        Assert.True(detector.Playing);
        Feed(detector, 0.2f, 1);   // the next song: the silence starts counting from nothing again
        Feed(detector, 0, 14);
        Assert.True(detector.Playing);

        Feed(detector, 0, 1);
        Assert.False(detector.Playing);
    }

    [Fact]
    public void Hiss_too_quiet_to_hear_is_silence()
    {
        var detector = new SoundDetector();

        Feed(detector, SoundDetector.Audible / 2, 30);

        Assert.False(detector.Playing);
    }

    // ---- Looking at the idle time as rarely as possible ----

    [Fact]
    public void While_the_laptop_is_in_use_the_idle_time_is_next_looked_at_when_a_rule_could_first_start()
    {
        TimeSpan[] limits = [TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(2)];

        var wait = IdleSchedule.NextCheck(TimeSpan.FromSeconds(20), limits);

        Assert.InRange(wait, TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(101));
    }

    [Fact]
    public void While_a_rule_is_active_the_user_coming_back_is_noticed_quickly()
    {
        TimeSpan[] limits = [TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(2)];

        Assert.Equal(IdleSchedule.WhileIdle, IdleSchedule.NextCheck(TimeSpan.FromMinutes(3), limits));
        Assert.Equal(IdleSchedule.WhileIdle, IdleSchedule.NextCheck(TimeSpan.FromMinutes(2), limits));
    }

    [Fact]
    public void The_idle_time_is_never_looked_at_in_a_tight_loop()
    {
        var wait = IdleSchedule.NextCheck(TimeSpan.FromMinutes(2) - TimeSpan.FromMilliseconds(1), [TimeSpan.FromMinutes(2)]);

        Assert.True(wait >= IdleSchedule.WhileIdle);
    }

    // ---- Which rule wins ----

    [Fact]
    public void First_rule_wins_each_setting_and_later_rules_fill_the_gaps()
    {
        AutomationRule[] rules =
        [
            new() { Trigger = AutomationTrigger.AppOpen, App = "game", Power = PowerProfile.Turbo },
            new() { Trigger = AutomationTrigger.OnBattery, Power = PowerProfile.Eco, LightingProfile = "dim" },
        ];

        var outcome = AutomationOutcome.Decide(rules, State(true, "game"));

        Assert.Equal(PowerProfile.Turbo, outcome.Power);
        Assert.Equal("dim", outcome.LightingProfile);
        Assert.Null(outcome.Fans);
    }

    [Fact]
    public void Nothing_is_wanted_when_no_rule_is_active()
    {
        Assert.Equal(default, AutomationOutcome.Decide([ForApp("game")], State()));
    }

    // ---- Knowing which apps are open ----

    [Fact]
    public void Tracker_finds_watched_apps_already_running()
    {
        var tracker = new AppTracker(app => app == "game");

        Assert.True(tracker.Watch(["game", "editor"]));

        Assert.Equal(["game"], tracker.Open);
    }

    [Fact]
    public void Tracker_ignores_programs_no_rule_names()
    {
        var tracker = new AppTracker(_ => false);
        tracker.Watch(["game"]);

        Assert.False(tracker.NoticeFront("browser"));

        Assert.Empty(tracker.Open);
    }

    [Fact]
    public void App_stays_open_after_leaving_the_front_until_it_closes()
    {
        bool running = false;
        var tracker = new AppTracker(_ => running);
        tracker.Watch(["game"]);

        running = true;
        Assert.True(tracker.NoticeFront("GAME"));
        Assert.False(tracker.NoticeFront("game"));   // already known
        Assert.False(tracker.Sweep());
        Assert.Contains("game", tracker.Open);

        running = false;
        Assert.True(tracker.Sweep());
        Assert.Empty(tracker.Open);
    }

    // ---- Changing things and putting them back ----

    sealed class FakeTarget : IAutomationTarget
    {
        public List<string> Calls = [];
        public Profile Lighting = new() { Name = "mine" };
        public PowerProfile Power = PowerProfile.Balanced;
        public (FanMode Mode, int Cpu, int Gpu) Fans = (FanMode.Custom, 65, 70);
        public bool HelperMissing;
        public TaskCompletionSource? Gate;
        public HashSet<string> Profiles = ["gaming", "dim"];

        public Profile CaptureLighting() => Lighting;

        public Profile? LoadProfile(string name) => Profiles.Contains(name) ? new Profile { Name = name } : null;

        public void ApplyLighting(Profile profile) { Lighting = profile; Calls.Add($"lighting {profile.Name}"); }

        public Task<ThermalStatus> GetStatusAsync() => Task.FromResult(Status());

        public async Task<ThermalStatus> SetPowerAsync(PowerProfile profile)
        {
            if (Gate is { } gate) await gate.Task;
            if (HelperMissing) return ThermalStatus.Failed("no helper");
            Power = profile;
            Calls.Add($"power {profile}");
            return Status();
        }

        public Task<ThermalStatus> SetFansAsync(FanMode mode, int cpu, int gpu)
        {
            if (HelperMissing) return Task.FromResult(ThermalStatus.Failed("no helper"));
            Fans = (mode, cpu, gpu);
            Calls.Add($"fans {mode} {cpu} {gpu}");
            return Task.FromResult(Status());
        }

        ThermalStatus Status() => HelperMissing
            ? ThermalStatus.Failed("no helper")
            : new ThermalStatus { Ok = true, Profile = Power, FanMode = Fans.Mode, CpuPercent = Fans.Cpu, GpuPercent = Fans.Gpu };
    }

    [Fact]
    public async Task Settings_return_to_how_they_were_when_the_rule_ends()
    {
        var target = new FakeTarget();
        var controller = new AutomationController(target);

        await controller.RequestAsync(new AutomationOutcome("gaming", PowerProfile.Turbo, FanMode.Max));
        Assert.Equal("gaming", target.Lighting.Name);
        Assert.Equal(PowerProfile.Turbo, target.Power);
        Assert.Equal(FanMode.Max, target.Fans.Mode);

        await controller.RequestAsync(default);
        Assert.Equal("mine", target.Lighting.Name);
        Assert.Equal(PowerProfile.Balanced, target.Power);
        Assert.Equal((FanMode.Custom, 65, 70), target.Fans);   // custom speeds come back too
    }

    [Fact]
    public async Task Moving_between_rules_keeps_the_original_setting_to_return_to()
    {
        var target = new FakeTarget();
        var controller = new AutomationController(target);

        await controller.RequestAsync(new AutomationOutcome("gaming", PowerProfile.Turbo, null));
        await controller.RequestAsync(new AutomationOutcome("dim", PowerProfile.Eco, null));
        await controller.RequestAsync(default);

        Assert.Equal("mine", target.Lighting.Name);
        Assert.Equal(PowerProfile.Balanced, target.Power);
    }

    [Fact]
    public async Task Settings_no_rule_mentions_are_not_touched()
    {
        var target = new FakeTarget();
        var controller = new AutomationController(target);

        await controller.RequestAsync(new AutomationOutcome("gaming", null, null));
        await controller.RequestAsync(default);

        Assert.Equal(["lighting gaming", "lighting mine"], target.Calls);
    }

    [Fact]
    public async Task The_setup_to_save_is_the_users_own_while_a_rule_is_showing_its_lighting()
    {
        var target = new FakeTarget();
        var controller = new AutomationController(target);

        await controller.RequestAsync(new AutomationOutcome("gaming", null, null));
        Assert.Equal("mine", controller.LightingBefore?.Name);

        await controller.RequestAsync(default);
        Assert.Null(controller.LightingBefore);
    }

    [Fact]
    public async Task A_deleted_profile_is_reported_and_the_lighting_stays()
    {
        var target = new FakeTarget();
        var controller = new AutomationController(target);

        await controller.RequestAsync(new AutomationOutcome("gone", null, null));

        Assert.Contains("gone", controller.LastProblem);
        Assert.Empty(target.Calls);
        Assert.Equal("mine", target.Lighting.Name);
    }

    [Fact]
    public async Task Lights_off_needs_no_saved_profile_and_the_lighting_comes_back_after()
    {
        var target = new FakeTarget();
        var controller = new AutomationController(target);

        await controller.RequestAsync(new AutomationOutcome(AutomationRule.LightsOff, null, null));

        Assert.Null(controller.LastProblem);
        Assert.Equal("static", target.Lighting.Effect);
        Assert.Equal("000000", target.Lighting.Color);
        Assert.False(target.Lighting.AutoColor);
        Assert.Null(target.Lighting.Overlay);
        Assert.Equal(-1, target.Lighting.Brightness);   // the brightness is left alone
        Assert.Null(target.Lighting.LightBar);          // and so is the light bar

        await controller.RequestAsync(default);
        Assert.Equal("mine", target.Lighting.Name);
    }

    [Fact]
    public async Task A_saved_profile_called_lights_off_is_used_instead_of_the_built_in_one()
    {
        var target = new FakeTarget();
        target.Profiles.Add(AutomationRule.LightsOff);
        var controller = new AutomationController(target);

        await controller.RequestAsync(new AutomationOutcome(AutomationRule.LightsOff, null, null));

        Assert.Equal(new Profile().Color, target.Lighting.Color);   // the fake's own profile, not black
    }

    [Fact]
    public async Task Without_the_helper_lighting_still_works_and_the_problem_is_reported()
    {
        var target = new FakeTarget { HelperMissing = true };
        var controller = new AutomationController(target);

        await controller.RequestAsync(new AutomationOutcome("gaming", PowerProfile.Turbo, FanMode.Max));

        Assert.Equal("gaming", target.Lighting.Name);
        Assert.Equal("no helper", controller.LastProblem);

        await controller.RequestAsync(default);   // nothing was remembered, so nothing is sent to put back
        Assert.Equal("mine", target.Lighting.Name);
        Assert.Null(controller.LastProblem);
    }

    [Fact]
    public async Task A_change_arriving_during_another_is_applied_afterwards()
    {
        var target = new FakeTarget { Gate = new TaskCompletionSource() };
        var controller = new AutomationController(target);

        var first = controller.RequestAsync(new AutomationOutcome(null, PowerProfile.Turbo, null));
        await controller.RequestAsync(default);   // the app closed while Turbo was still being set
        target.Gate.SetResult();
        await first;

        Assert.Equal(["power Turbo", "power Balanced"], target.Calls);
    }

    [Fact]
    public async Task Quitting_puts_back_power_and_fans()
    {
        var target = new FakeTarget();
        var controller = new AutomationController(target);
        await controller.RequestAsync(new AutomationOutcome(null, PowerProfile.Turbo, FanMode.Max));

        await controller.RestoreHelperSettingsAsync();

        Assert.Equal(PowerProfile.Balanced, target.Power);
        Assert.Equal((FanMode.Custom, 65, 70), target.Fans);
    }

    // ---- Saving ----

    [Fact]
    public void Rules_survive_saving_and_loading()
    {
        var path = Path.Combine(Path.GetTempPath(), $"raptorsense-automations-{Guid.NewGuid():N}.json");
        try
        {
            var rule = new AutomationRule { App = "game.exe", LightingProfile = "gaming", Power = PowerProfile.Turbo, Fans = FanMode.Max };
            AutomationStore.Save(new AutomationSettings { Enabled = false, Rules = [rule] }, path);

            var loaded = AutomationStore.Load(path);

            Assert.False(loaded.Enabled);
            Assert.Equal(rule, Assert.Single(loaded.Rules));
            var text = File.ReadAllText(path);
            Assert.Contains("\"Turbo\"", text);   // readable by people
            Assert.DoesNotContain("AppName", text);   // worked out from App, not a setting
            Assert.DoesNotContain("Idle", text);      // only written for a rule that sets it
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void The_new_kinds_of_rule_survive_saving_and_loading()
    {
        var path = Path.Combine(Path.GetTempPath(), $"raptorsense-automations-{Guid.NewGuid():N}.json");
        try
        {
            AutomationRule[] rules =
            [
                new() { Trigger = AutomationTrigger.SoundPlaying, LightingProfile = "gaming" },
                new() { Trigger = AutomationTrigger.Idle, IdleMinutes = 12, LightingProfile = AutomationRule.LightsOff },
                new() { Trigger = AutomationTrigger.ScreenOff, LightingProfile = AutomationRule.LightsOff },
            ];
            AutomationStore.Save(new AutomationSettings { Rules = rules }, path);

            Assert.Equal(rules, AutomationStore.Load(path).Rules);
            var text = File.ReadAllText(path);
            Assert.Contains("\"SoundPlaying\"", text);
            Assert.Contains("\"IdleMinutes\": 12", text);
            Assert.DoesNotContain("IdleAfter", text);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_damaged_file_gives_no_rules_rather_than_an_error()
    {
        var path = Path.Combine(Path.GetTempPath(), $"raptorsense-automations-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ not json");
            Assert.Empty(AutomationStore.Load(path).Rules);
        }
        finally { File.Delete(path); }
    }
}
