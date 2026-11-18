using System;
using System.Collections.Generic;
using System.Globalization;

namespace ProjectC.Client
{
    public static class YakouUiText
    {
        private static readonly IReadOnlyDictionary<string, string> Phases = new Dictionary<string, string>
        {
            ["TurnStart"] = "ui.yakou.phase.turn_start",
            ["Main"] = "ui.yakou.phase.main",
            ["AssignBlockers"] = "ui.yakou.phase.assign_blockers",
            ["RaidResponse"] = "ui.yakou.phase.raid_response",
            ["SpellResponse"] = "ui.yakou.phase.spell_response",
            ["Finished"] = "ui.yakou.phase.finished",
        };

        private static readonly IReadOnlyDictionary<string, string> Methods = new Dictionary<string, string>
        {
            ["Normal"] = "keyword_NormalSummon_name",
            ["Devour"] = "keyword_Devour_name",
            ["Necromancy"] = "keyword_Necromancy_name",
            ["Discard"] = "keyword_Discard_name",
        };

        private static readonly IReadOnlyDictionary<string, string> SelectionOperations = new Dictionary<string, string>
        {
            ["LookAndChoose"] = "ui.yakou.dev.operation.look_and_choose",
            ["Salvage"] = "ui.yakou.dev.operation.salvage",
            ["ModifyCountdown"] = "ui.yakou.dev.operation.modify_countdown",
            ["SetCountdown"] = "ui.yakou.dev.operation.modify_countdown",
            ["HealAccumulatedDamage"] = "ui.yakou.dev.operation.heal_damage",
        };

        private static readonly IReadOnlyDictionary<string, string> SourceZones = new Dictionary<string, string>
        {
            ["FriendlyHand"] = "ui.yakou.dev.zone.friendly_hand",
            ["FriendlyField"] = "ui.yakou.dev.zone.friendly_field",
            ["FriendlyGraveyard"] = "ui.yakou.dev.zone.friendly_graveyard",
        };

        private static readonly IReadOnlyDictionary<string, string> Errors = new Dictionary<string, string>
        {
            ["ConnectionLostDuringBinding"] = "ui.yakou.status.binding_lost",
            ["UnknownYakouCommand"] = "ui.yakou.error.unknown_command",
            ["AlreadyJoined"] = "ui.yakou.error.already_joined",
            ["MatchLimit"] = "ui.yakou.error.match_limit",
            ["MatchNotFound"] = "ui.yakou.error.match_not_found",
            ["MatchFull"] = "ui.yakou.error.match_full",
            ["InvalidResumeToken"] = "ui.yakou.error.invalid_resume_token",
            ["NotJoined"] = "ui.yakou.error.not_joined",
            ["WaitingForOpponent"] = "ui.yakou.error.waiting_for_opponent",
            ["DuplicateRequestId"] = "ui.yakou.error.duplicate_request",
            ["RequestCacheFull"] = "ui.yakou.error.request_cache_full",
            ["PlayerLeft"] = "ui.yakou.error.player_left",
            ["Expired"] = "ui.yakou.error.expired",
            ["ServerShutdown"] = "ui.yakou.error.server_shutdown",
            ["Reconnected"] = "ui.yakou.error.reconnected",
            ["Rejected"] = "ui_battle_action_unavailable",
            ["InvalidNumericId"] = "ui.yakou.dev.numeric_id_error",
        };

        public static string Get(string key, params object[] arguments)
        {
            var localization = CardPresentation.YakouLibrary.Localization;
            localization.Language = CardPresentation.Library.Localization.Language;
            string value = localization.GetText(key);
            return arguments.Length == 0 ? value : string.Format(CultureInfo.InvariantCulture, value, arguments);
        }

        public static string Phase(string phase)
        {
            return Get(Phases.TryGetValue(phase ?? string.Empty, out string key)
                ? key : "ui.yakou.error.generic");
        }

        public static string Method(string method)
        {
            return Get(Methods.TryGetValue(method ?? string.Empty, out string key)
                ? key : "ui.yakou.error.generic");
        }

        public static string Player(int seat, int ownSeat)
        {
            return Get(seat == 0 ? "ui.yakou.player.none"
                : seat == ownSeat ? "ui.log.own" : "ui.log.opponent");
        }

        public static string SelectionStatus(string status)
        {
            return Get(status == "Ready" ? "ui.yakou.dev.selection_ready"
                : "ui.yakou.dev.selection_unavailable");
        }

        public static string SelectionOperation(string operation)
        {
            return Get(SelectionOperations.TryGetValue(operation ?? string.Empty, out string key)
                ? key : "ui.yakou.error.generic");
        }

        public static string SourceZone(string zone)
        {
            return Get(SourceZones.TryGetValue(zone ?? string.Empty, out string key)
                ? key : "ui.yakou.error.generic");
        }

        public static string Error(string reason)
        {
            if (string.IsNullOrEmpty(reason))
            {
                return string.Empty;
            }
            return Get(Errors.TryGetValue(reason, out string key) ? key : "ui.yakou.error.generic");
        }
    }
}
