using System;
using System.Collections.Generic;
using System.Linq;
using BattleTech;
using BattleTech.UI;
using Harmony;
using HBS;
using UnityEngine;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global
// ReSharper disable once ClassNeverInstantiated.Global
// ReSharper disable InconsistentNaming

namespace Retrainer
{
    public class Retrainer
    {
        [HarmonyPatch(typeof(SGBarracksMWDetailPanel), nameof(SGBarracksMWDetailPanel.OnSkillsSectionClicked), MethodType.Normal)]
        public static class SGBarracksMWDetailPanel_OnSkillsSectionClicked_Patch
        {
            private static readonly UIColorRef backfill = LazySingletonBehavior<UIManager>.Instance.UILookAndColorConstants.PopupBackfill;

            public static bool Prefix(SGBarracksMWDetailPanel __instance, Pilot ___curPilot)
            {
                var hotkeyPerformed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (!hotkeyPerformed)
                {
                    return true;
                }

                var simState = UnityGameInstance.BattleTechGame.Simulation;
                if (Main.Settings.OnceOnly && ___curPilot.pilotDef.PilotTags.Contains("HasRetrained"))
                {
                    GenericPopupBuilder
                        .Create("Unable To Retrain", "Each pilot can only retrain once.")
                        .AddButton("Acknowledged")
                        .CancelOnEscape()
                        .AddFader(backfill)
                        .Render();
                    return true;
                }

                if (Main.Settings.TrainingModuleRequired && !simState.ShipUpgrades.Any(u => u.Tags.Any(t => t.Contains("argo_trainingModule2"))))
                {
                    GenericPopupBuilder
                        .Create("Unable To Retrain", "You must have built the Training Module 2 upgrade aboard the Argo.")
                        .AddButton("Acknowledged")
                        .CancelOnEscape()
                        .AddFader(backfill)
                        .Render();
                    return true;
                }

                var sim = UnityGameInstance.BattleTechGame.Simulation;
                var cost = GetRespecCost(sim, ___curPilot);
                var days = GetRespecInjuryDays(sim, ___curPilot);
                if (simState.Funds < Main.Settings.Cost)
                {
                    GenericPopupBuilder
                        .Create("Unable To Retrain", $"You need ¢{cost:N0}.")
                        .AddButton("Acknowledged")
                        .CancelOnEscape()
                        .AddFader(backfill)
                        .Render();
                    return true;
                }
                var message = "This will set skills back to 1 and refund all XP.";
                //var message = "This will set skills back to their intial values and refund all XP.";

                message += $"\nIt will cost ¢{cost:N0}";

                message += (days > 0) ? $" and the pilot will be unavailable for {days:N0} days."
                                            : ".";

                message += Main.Settings.OnceOnly ? $"\nEach pilot can only retrain once."
                                                  : "";

                GenericPopupBuilder
                    .Create("Retrain", message)
                    .AddButton("Cancel")
                    .AddButton("Retrain Pilot", () => RespecAndRefresh(__instance, ___curPilot))
                    .CancelOnEscape()
                    .AddFader(backfill)
                    .Render();
                return false;
            }
        }

        private static void RespecAndRefresh(SGBarracksMWDetailPanel __instance, Pilot pilot)
        {
            Logger.Log($"===============================================================");
            Logger.Log($"Before:\t{pilot.pilotDef.ExperienceSpent}, {pilot.pilotDef.ExperienceUnspent}");
            Logger.Log($"Skill:\t{pilot.pilotDef.SkillGunnery}, {pilot.pilotDef.SkillPiloting}, {pilot.pilotDef.SkillGuts}, {pilot.pilotDef.SkillTactics}");
            Logger.Log($"Base:\t{pilot.pilotDef.BaseGunnery}, {pilot.pilotDef.BasePiloting}, {pilot.pilotDef.BaseGuts}, {pilot.pilotDef.BaseTactics}");
            Logger.Log($"Bonus:\t{pilot.pilotDef.BonusGunnery}, {pilot.pilotDef.BonusPiloting}, {pilot.pilotDef.BonusGuts}, {pilot.pilotDef.BonusTactics}");

            SimGameState sim = UnityGameInstance.BattleTechGame.Simulation;
            int cost = GetRespecCost(sim, pilot);
            int days = GetRespecInjuryDays(sim, pilot) + sim.GetPilotTimeoutTimeRemaining(pilot);

            RespecPilot(pilot);
            sim.AddFunds(-cost);
            
            // currently bugged: AddInjuryDays(sim, pilot, days);
            pilot.pilotDef.SetTimeoutTime(days);
            pilot.pilotDef.PilotTags.Add("pilot_lightinjury");
            sim.RefreshInjuries();

            pilot.pilotDef.PilotTags.Add("HasRetrained");
            __instance.DisplayPilot(pilot);
            __instance.ForceRefreshImmediate();
            sim.RoomManager.RefreshDisplay();
            Logger.Log($"===============================================================");
            Logger.Log($"cost: {cost}, days: {days}");
            Logger.Log($"===============================================================");
            Logger.Log($"After:\t{pilot.pilotDef.ExperienceSpent}, {pilot.pilotDef.ExperienceUnspent}");
            Logger.Log($"Skill:\t{pilot.pilotDef.SkillGunnery}, {pilot.pilotDef.SkillPiloting}, {pilot.pilotDef.SkillGuts}, {pilot.pilotDef.SkillTactics}");
            Logger.Log($"Base:\t{pilot.pilotDef.BaseGunnery}, {pilot.pilotDef.BasePiloting}, {pilot.pilotDef.BaseGuts}, {pilot.pilotDef.BaseTactics}");
            Logger.Log($"Bonus:\t{pilot.pilotDef.BonusGunnery}, {pilot.pilotDef.BonusPiloting}, {pilot.pilotDef.BonusGuts}, {pilot.pilotDef.BonusTactics}");

            //invoking private method:  sim.RespecPilot(pilot); // bugged method!!!
            //typeof(ToHit).GetMethod("RespecPilot", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(sim, new object[] { pilot });
            //pilot.AddExperience(0, "respec", experienceSpent);
        }


        private static int GetLevelRangeCostAdjusted(int first, int last, float adjustment)
        {
            int cost = 0;
            for (int i = first; i <= last; ++i)
            {
                cost += Mathf.CeilToInt(adjustment * 100.0f * Mathf.Pow(i, adjustment * 2.0f));
            }
            return cost;
        }


        private static int GetTrueSpentExperienceValue(SimGameState sim, Pilot pilot)
        {
            var gunnery = sim.GetLevelRangeCost(1, pilot.pilotDef.SkillGunnery - 1);
            var piloting = sim.GetLevelRangeCost(1, pilot.pilotDef.SkillPiloting - 1);
            var guts = sim.GetLevelRangeCost(1, pilot.pilotDef.SkillGuts - 1);
            var tactics = sim.GetLevelRangeCost(1, pilot.pilotDef.SkillTactics - 1);

            // It's a bit dodgy to do this here... but...
            //"pilot_assassin" : "Decreased XP cost for leveling Gunnery."
            //"pilot_bookish" : "Decreased XP cost for leveling Tactics. Increased XP cost for leveling Guts." ,
            //"pilot_klutz" : "Increased XP cost to level Piloting and a small chance to eject when knocked down.",
            //NB: reflects cost to reach their current levels from 1, not from where they actually started...
            if (pilot.pilotDef.PilotTags.Contains("pilot_assassin"))
            {
                gunnery = GetLevelRangeCostAdjusted(1, pilot.pilotDef.SkillGunnery - 1, 0.85f);
                Logger.Log($"Assassin gunnery: normal value would be = {sim.GetLevelRangeCost(1, pilot.pilotDef.SkillGunnery - 1)}, actual value = {gunnery}");
            }
            if (pilot.pilotDef.PilotTags.Contains("pilot_bookish"))
            {
                tactics = GetLevelRangeCostAdjusted(1, pilot.pilotDef.SkillTactics - 1, 0.85f);
                Logger.Log($"Bookish tactics: normal value would be = {sim.GetLevelRangeCost(1, pilot.pilotDef.SkillTactics - 1)}, actual value = {tactics}");
                guts = GetLevelRangeCostAdjusted(1, pilot.pilotDef.SkillGuts - 1, 1.15f);
                Logger.Log($"Bookish guts: normal value would be = {sim.GetLevelRangeCost(1, pilot.pilotDef.SkillGuts - 1)}, actual value = {guts}");
            }
            if (pilot.pilotDef.PilotTags.Contains("pilot_klutz"))
            {
                piloting = GetLevelRangeCostAdjusted(1, pilot.pilotDef.SkillPiloting - 1, 1.15f);
                Logger.Log($"Klutz piloting: normal value would be = {sim.GetLevelRangeCost(1, pilot.pilotDef.SkillPiloting - 1)}, actual value = {piloting}");
            }

            //Logger.Log($"Skill:\t{pilot.pilotDef.SkillGunnery}, {pilot.pilotDef.SkillPiloting}, {pilot.pilotDef.SkillGuts}, {pilot.pilotDef.SkillTactics}");
            //Logger.Log($"Base:\t{pilot.pilotDef.BaseGunnery}, {pilot.pilotDef.BasePiloting}, {pilot.pilotDef.BaseGuts}, {pilot.pilotDef.BaseTactics}");
            //Logger.Log($"Bonus:\t{pilot.pilotDef.BonusGunnery}, {pilot.pilotDef.BonusPiloting}, {pilot.pilotDef.BonusGuts}, {pilot.pilotDef.BonusTactics}");
            //Logger.Log($"Spent:\t{gunnery} + {piloting} + {guts} + {tactics} = {gunnery + piloting + guts + tactics} =? {pilot.pilotDef.ExperienceSpent + pilot.pilotDef.ExperienceUnspent} = {pilot.pilotDef.ExperienceSpent} + {pilot.pilotDef.ExperienceUnspent}");

            //var gunnery2 = sim.GetLevelRangeCost(pilot.pilotDef.BaseGunnery, pilot.pilotDef.SkillGunnery - 1);
            //var piloting2 = sim.GetLevelRangeCost(pilot.pilotDef.BasePiloting, pilot.pilotDef.SkillPiloting - 1);
            //var guts2 = sim.GetLevelRangeCost(pilot.pilotDef.BaseGuts, pilot.pilotDef.SkillGuts - 1);
            //var tactics2 = sim.GetLevelRangeCost(pilot.pilotDef.BaseTactics, pilot.pilotDef.SkillTactics - 1);

            //Logger.Log($"Skill/Base diff:\t{gunnery2} + {piloting2} + {guts2} + {tactics2} = {gunnery2 + piloting2 + guts2 + tactics2} =? {pilot.pilotDef.ExperienceSpent} spent");

            return gunnery + piloting + guts + tactics;
        }


        private static int GetExperienceRefundAmount(SimGameState sim, Pilot pilot)
        {
            return GetTrueSpentExperienceValue(sim, pilot);
            //return pilot.pilotDef.ExperienceSpent;
        }


        private static int GetRespecCost(SimGameState sim, Pilot pilot)
        {
            if (!Main.Settings.VariableCreditsCost)
            {
                return Main.Settings.Cost;
            }
            return (int)(GetExperienceRefundAmount(sim, pilot) * Main.Settings.CreditsPerExperience);
        }


        private static int GetRespecInjuryDays(SimGameState sim, Pilot pilot)
        {
            if (!Main.Settings.RespecCausesInjury || Main.Settings.MedtechPerExperience <= 0)
            {
                return 0;
            }
            var experience = GetExperienceRefundAmount(sim, pilot);
            var medtech = experience * Main.Settings.MedtechPerExperience;
            if (medtech <= 0)
            {
                return 0;
            }
            int days = (int)Math.Ceiling((double)medtech / sim.MedTechSkill);
            Logger.Log($"experience refund: {experience}");
            Logger.Log($"medtech point cost: {medtech}");
            Logger.Log($"sim.MedTechSkill: {sim.MedTechSkill}");
            Logger.Log($"GetRespecInjuryTime: {days}");
            return days;
        }


        //private static void AddInjuryDays(SimGameState sim, Pilot pilot, int days)
        //{
        //    int timeoutRemainingDays = sim.GetPilotTimeoutTimeRemaining(pilot);
        //    Logger.Log($"Applying injury time: (existing: {timeoutRemainingDays}) + (new: {days})");
        //    pilot.pilotDef.SetTimeoutTime(timeoutRemainingDays + days);
        //    pilot.pilotDef.PilotTags.Add("pilot_lightinjury");
        //    sim.RefreshInjuries();

        //    //int timeoutRemainingDays = 0;
        //    //if (pilot.pilotDef.TimeoutRemaining > 0)
        //    //{
        //    //    var med = sim.MedBayQueue.GetSubEntry(pilot.Description.Id);
        //    //    Logger.Log($"med.GetRemainingCost: {med.GetRemainingCost()}");
        //    //    Logger.Log($"med.GetCostPaid: {med.GetCostPaid()}");
        //    //    Logger.Log($"med.GetCost: {med.GetCost()}");
        //    //    Logger.Log($"sim.GetDailyHealValue: {sim.GetDailyHealValue()}");
        //    //    timeoutRemainingDays = (int)Math.Ceiling((double)med.GetRemainingCost() / sim.GetDailyHealValue());
        //    //    sim.MedBayQueue.RemoveSubEntry(pilot.Description.Id);
        //    //}
        //    //Logger.Log($"timeoutRemainingDays: {timeoutRemainingDays}, adding: {days}");
        //    //pilot.pilotDef.SetTimeoutTime(timeoutRemainingDays + days);
        //    //pilot.pilotDef.PilotTags.Add("pilot_lightinjury");

        //    //this.RoomManager.RefreshTimeline(false);

        //}



        private static void RespecPilot(Pilot pilot)
        {
            SimGameState sim = UnityGameInstance.BattleTechGame.Simulation;
            PilotDef pilotDef = pilot.pilotDef.CopyToSim();
            foreach (string value in sim.Constants.Story.CampaignCommanderUpdateTags)
            {
                if (!sim.CompanyTags.Contains(value))
                {
                    sim.CompanyTags.Add(value);
                }
            }
            //int num = 0;
            //if (pilotDef.BonusPiloting > 0)
            //{
            //    num += sim.GetLevelRangeCost(pilotDef.BasePiloting, pilotDef.SkillPiloting - 1);
            //}
            //if (pilotDef.BonusGunnery > 0)
            //{
            //    num += sim.GetLevelRangeCost(pilotDef.BaseGunnery, pilotDef.SkillGunnery - 1);
            //}
            //if (pilotDef.BonusGuts > 0)
            //{
            //    num += sim.GetLevelRangeCost(pilotDef.BaseGuts, pilotDef.SkillGuts - 1);
            //}
            //if (pilotDef.BonusTactics > 0)
            //{
            //    num += sim.GetLevelRangeCost(pilotDef.BaseTactics, pilotDef.SkillTactics - 1);
            //}
            //if (num <= 0)
            //{
            //    return;
            //}

            // alternate: broken because some pilots start with values in this:
            //int num = pilot.pilotDef.ExperienceSpent;

            int num = GetTrueSpentExperienceValue(sim, pilot);
            Logger.Log($"num: {num}");
            // nerf the pilotdef
            Traverse.Create(pilotDef).Property("BaseGunnery").SetValue(1);
            Traverse.Create(pilotDef).Property("BasePiloting").SetValue(1);
            Traverse.Create(pilotDef).Property("BaseGuts").SetValue(1);
            Traverse.Create(pilotDef).Property("BaseTactics").SetValue(1);

            pilotDef.abilityDefNames.Clear();
            List<string> abilities = SimGameState.GetAbilities(pilotDef.BaseGunnery, pilotDef.BasePiloting, pilotDef.BaseGuts, pilotDef.BaseTactics);
            pilotDef.abilityDefNames.AddRange(abilities);
            pilotDef.SetSpentExperience(0);
            pilotDef.ForceRefreshAbilityDefs();
            pilotDef.ResetBonusStats();
            pilot.FromPilotDef(pilotDef);
            pilot.AddExperience(0, "Respec", num);
        }


    }
}
