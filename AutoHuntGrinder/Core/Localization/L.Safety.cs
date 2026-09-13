namespace AutoHuntGrinder.Core.Localization;

internal static partial class L
{
    internal static class Safety
    {
        public static readonly LocString Add = new("safety.add", "Add");
        public static readonly LocString Remove = new("safety.remove", "Remove");
        public static readonly LocString Test = new("safety.test", "Test");
        public static readonly LocString Preview = new("safety.preview", "Preview");

        public static readonly LocString CatRepair = new("safety.cat.repair", "Repair");
        public static readonly LocString CatRepairSub = new("safety.cat.repairSub", "Auto-repair gear when equipped item condition drops below the threshold.");
        public static readonly LocString CatConsumables = new("safety.cat.consumables", "Consumables");
        public static readonly LocString CatConsumablesSub = new("safety.cat.consumablesSub", "Keep food and medicine buffs up while hunting: Well Fed is a free +3% EXP.");
        public static readonly LocString CatHumanizer = new("safety.cat.humanizer", "Humanizer");
        public static readonly LocString CatHumanizerSub = new("safety.cat.humanizerSub", "Take periodic city breaks between marks: teleport to a random hub and wander around for a few minutes before resuming.");
        public static readonly LocString CatPartyInvites = new("safety.cat.partyInvites", "Party invites");
        public static readonly LocString CatPartyInvitesSub = new("safety.cat.partyInvitesSub", "Auto-decline incoming party invites during a run, after a human-like delay, with an optional reply.");
        public static readonly LocString CatGmAlert = new("safety.cat.gmAlert", "GM alert");
        public static readonly LocString CatGmAlertSub = new("safety.cat.gmAlertSub", "Detects nearby Game Masters and reacts: stop the bot, ping you, or take more drastic action.");

        public static readonly LocString RepairTrigger = new("safety.repair.trigger", "Repair trigger");
        public static readonly LocString AutoRepair = new("safety.repair.autoRepair", "Auto-repair gear");
        public static readonly LocString AutoRepairHelp = new("safety.repair.autoRepairHelp", "Between marks, when the lowest equipped item drops to or below the threshold, the plugin runs a repair. At 0% the gear stops working, so keep some margin.");
        public static readonly LocString AutoRepairOff = new("safety.repair.autoRepairOff", "Auto-repair is off. Enable it to configure repair.");
        public static readonly LocString RepairThreshold = new("safety.repair.threshold", "Repair threshold");
        public static readonly LocString RepairThresholdHelp = new("safety.repair.thresholdHelp", "Trips when the worst equipped slot reaches this condition percentage. 20% leaves comfortable margin before the 0% breakdown.");
        public static readonly LocString RepairSource = new("safety.repair.source", "Repair source");
        public static readonly LocString RepairSourceHelp = new("safety.repair.sourceHelp", "How the repair is performed. Self-repair uses Dark Matter from your bag (no travel). NPC repair travels to your Grand Company mender.");
        public static readonly LocString RepairSelfThenNpcName = new("safety.repair.selfThenNpc.name", "Self, then NPC");
        public static readonly LocString RepairSelfThenNpcDetail = new("safety.repair.selfThenNpc.detail", "Use Dark Matter from your bag first; fall back to the Grand Company mender when you run out.");
        public static readonly LocString RepairSelfOnlyName = new("safety.repair.selfOnly.name", "Self only");
        public static readonly LocString RepairSelfOnlyDetail = new("safety.repair.selfOnly.detail", "Repair with Dark Matter from your bag. No travel.");
        public static readonly LocString RepairNpcOnlyName = new("safety.repair.npcOnly.name", "NPC only");
        public static readonly LocString RepairNpcOnlyDetail = new("safety.repair.npcOnly.detail", "Travel to your Grand Company mender (or a custom NPC) and repair there.");
        public static readonly LocString CustomNpc = new("safety.repair.customNpc", "Custom repair NPC");
        public static readonly LocString CustomNpcHelp = new("safety.repair.customNpcHelp", "Optional. Travel to any repair NPC instead of the Grand Company mender. Target the NPC in-game, then click \"Set from target\". Clear to fall back to the GC mender.");
        public static readonly LocString NpcNote = new("safety.repair.npcNote", "NPC repair uses your custom repair NPC if set, otherwise your Grand Company mender, teleporting there when needed. A custom NPC removes the Grand Company requirement.");
        public static readonly LocString NpcSet = new("safety.repair.npcSet", "{0}  ({1})");
        public static readonly LocString NpcNone = new("safety.repair.npcNone", "None: the Grand Company mender is used.");
        public static readonly LocString SetFromTarget = new("safety.repair.setFromTarget", "Set from target");
        public static readonly LocString NoTargetChat = new("safety.repair.noTargetChat", "No target. Target a repair NPC first, then click again.");
        public static readonly LocString NpcSetChat = new("safety.repair.npcSetChat", "Custom repair NPC set: {0} ({1}).");

        public static readonly LocString ConsumablesGroup = new("safety.consumables.group", "Consumables");
        public static readonly LocString AutoConsume = new("safety.consumables.autoConsume", "Auto-consume food & medicine");
        public static readonly LocString AutoConsumeHelp = new("safety.consumables.autoConsumeHelp", "Use food and medicine between marks to keep their buffs up; Well Fed alone is a free +3% EXP. Items are consumed only when out of combat, and refreshed before the buff runs out.");
        public static readonly LocString AutoConsumeOff = new("safety.consumables.autoConsumeOff", "Auto-consume is off. Enable it to pick items.");
        public static readonly LocString RefreshUnder = new("safety.consumables.refreshUnder", "Refresh when under");
        public static readonly LocString RefreshUnderHelp = new("safety.consumables.refreshUnderHelp", "Re-consume once the buff has fewer than this many minutes left. 0 only re-applies after it fully wears off. A meal lasts 30 minutes.");
        public static readonly LocString RefreshWornOff = new("safety.consumables.refreshWornOff", "only when worn off");
        public static readonly LocString RefreshFormat = new("safety.consumables.refreshFormat", "%d min left");
        public static readonly LocString ConsumablesItems = new("safety.consumables.items", "Items");
        public static readonly LocString AddItem = new("safety.consumables.addItem", "Add an item");
        public static readonly LocString AddItemHelp = new("safety.consumables.addItemHelp", "Pick from the food and medicine in your bag. HQ is used automatically when you have it.");
        public static readonly LocString ActiveItems = new("safety.consumables.activeItems", "Active items");
        public static readonly LocString ActiveItemsHelp = new("safety.consumables.activeItemsHelp", "Each is kept active in order; the next available one is consumed if the first runs out.");
        public static readonly LocString NoneInBag = new("safety.consumables.noneInBag", "No food or medicine in your bag. Stock some, then add it here.");
        public static readonly LocString KindFood = new("safety.consumables.kindFood", "Food");
        public static readonly LocString KindMedicine = new("safety.consumables.kindMedicine", "Medicine");
        public static readonly LocString Added = new("safety.consumables.added", "(added)");
        public static readonly LocString ItemLabel = new("safety.consumables.itemLabel", "{0}  [{1}]{2}");
        public static readonly LocString AlreadyAdded = new("safety.consumables.alreadyAdded", "Already added.");
        public static readonly LocString NoItemsAdded = new("safety.consumables.noItemsAdded", "No items added, so nothing will be consumed.");
        public static readonly LocString WellFed = new("safety.consumables.wellFed", "Well Fed");
        public static readonly LocString Medicated = new("safety.consumables.medicated", "Medicated");
        public static readonly LocString NoneInBagShort = new("safety.consumables.noneInBagShort", "{0}, none in bag");

        public static readonly LocString HumanizerBreaks = new("safety.humanizer.breaks", "Breaks");
        public static readonly LocString HumanizerEnable = new("safety.humanizer.enable", "Take periodic city breaks");
        public static readonly LocString HumanizerEnableHelp = new("safety.humanizer.enableHelp", "Every N marks, teleport to a random selected city and wander around for a few minutes before resuming. Helps you avoid player reports by acting a little more human, useful when you leave the PC running for long sessions and don't want others noticing you hunting non-stop.");
        public static readonly LocString HumanizerOff = new("safety.humanizer.off", "Humanizer is off. Enable it to configure breaks.");
        public static readonly LocString MarksBetween = new("safety.humanizer.marksBetween", "Marks between breaks");
        public static readonly LocString MarksBetweenHelp = new("safety.humanizer.marksBetweenHelp", "Take a break after this many mark kills. The count starts over after each break, and whenever a run starts or resumes.");
        public static readonly LocString MarksFormat = new("safety.humanizer.marksFormat", "%d marks");
        public static readonly LocString BreakLength = new("safety.humanizer.breakLength", "Break length");
        public static readonly LocString BreakLengthHelp = new("safety.humanizer.breakLengthHelp", "A random duration between these two values is rolled for each break.");
        public static readonly LocString MinutesFormat = new("safety.humanizer.minutesFormat", "%d min");
        public static readonly LocString HumanizerWandering = new("safety.humanizer.wandering", "Wandering");
        public static readonly LocString PauseBetween = new("safety.humanizer.pauseBetween", "Pause between walks");
        public static readonly LocString PauseBetweenHelp = new("safety.humanizer.pauseBetweenHelp", "After arriving at each random point, stand still for a random duration in this range before walking somewhere else.");
        public static readonly LocString SecondsFormat = new("safety.humanizer.secondsFormat", "%d s");
        public static readonly LocString WalkDistance = new("safety.humanizer.walkDistance", "Walk distance");
        public static readonly LocString WalkDistanceHelp = new("safety.humanizer.walkDistanceHelp", "Each random destination is rolled this many meters away from your current position. Larger ranges cover more of the city; smaller ranges keep you near the aetheryte.");
        public static readonly LocString MetersFormat = new("safety.humanizer.metersFormat", "%d m");
        public static readonly LocString HumanizerCities = new("safety.humanizer.cities", "Cities");
        public static readonly LocString AllowedCities = new("safety.humanizer.allowedCities", "Allowed cities");
        public static readonly LocString AllowedCitiesHelp = new("safety.humanizer.allowedCitiesHelp", "Tick the cities the plugin is allowed to teleport to. One is picked at random each break. Untick cities you haven't unlocked or don't want visited.");
        public static readonly LocString NoCities = new("safety.humanizer.noCities", "No cities selected, so the Humanizer skips the break and keeps hunting.");

        public static readonly LocString InvitesDecline = new("safety.invites.decline", "Decline");
        public static readonly LocString AutoDecline = new("safety.invites.autoDecline", "Auto-decline party invites");
        public static readonly LocString AutoDeclineHelp = new("safety.invites.autoDeclineHelp", "While a hunt is running, automatically decline incoming party invites after a short random delay. Invites that arrive while idle or playing manually are left alone for you to handle.");
        public static readonly LocString AutoDeclineOff = new("safety.invites.autoDeclineOff", "Auto-decline is off. Enable it to configure it.");
        public static readonly LocString DeclineDelay = new("safety.invites.delay", "Decline delay");
        public static readonly LocString DeclineDelayHelp = new("safety.invites.delayHelp", "Wait a random time in this range before declining, so it looks like you noticed the popup and dismissed it yourself.");
        public static readonly LocString InvitesReply = new("safety.invites.reply", "Reply");
        public static readonly LocString SendReply = new("safety.invites.sendReply", "Send a reply");
        public static readonly LocString SendReplyHelp = new("safety.invites.sendReplyHelp", "After declining, send a chat message so it reads like a polite human brush-off rather than an instant silent decline.");
        public static readonly LocString ReplyChannel = new("safety.invites.channel", "Reply channel");
        public static readonly LocString ReplyChannelHelp = new("safety.invites.channelHelp", "Where the message goes. \"Tell inviter\" whispers the person who invited you. Ignored when your message starts with a slash command.");
        public static readonly LocString ChannelTellName = new("safety.invites.channelTell.name", "Tell inviter");
        public static readonly LocString ChannelTellDetail = new("safety.invites.channelTell.detail", "Whisper the person who invited you.");
        public static readonly LocString ChannelSayName = new("safety.invites.channelSay.name", "Say");
        public static readonly LocString ChannelSayDetail = new("safety.invites.channelSay.detail", "Local /say, heard by players near you.");
        public static readonly LocString ChannelYellName = new("safety.invites.channelYell.name", "Yell");
        public static readonly LocString ChannelYellDetail = new("safety.invites.channelYell.detail", "Zone-wide /yell.");
        public static readonly LocString ReplyMessage = new("safety.invites.message", "Reply message");
        public static readonly LocString ReplyMessageHelp = new("safety.invites.messageHelp", "Use {name} for the inviter's character name and {world} for their home world. If the message begins with \"/\", it's sent verbatim as a command (e.g. /tell {name}@{world} busy right now!).");
        public static readonly LocString ReplyMessageHint = new("safety.invites.messageHint", "Sorry {name}, I'm busy right now!");

        public static readonly LocString GmAlerts = new("safety.gm.alerts", "Alerts");
        public static readonly LocString GmStopRun = new("safety.gm.stopRun", "Stop the run");
        public static readonly LocString GmStopRunHelp = new("safety.gm.stopRunHelp", "Halt automation immediately when a GM appears nearby. Strongly recommended; the rest of the alerts are useless if the bot keeps hunting.");
        public static readonly LocString GmToast = new("safety.gm.toast", "Toast notification");
        public static readonly LocString GmToastHelp = new("safety.gm.toastHelp", "Pop a Dalamud toast: \"GM <name> is nearby!\"");
        public static readonly LocString GmChat = new("safety.gm.chat", "Chat alert");
        public static readonly LocString GmChatHelp = new("safety.gm.chatHelp", "Print a red chat warning into your local log.");
        public static readonly LocString GmSound = new("safety.gm.sound", "Sound beeps");
        public static readonly LocString GmSoundHelp = new("safety.gm.soundHelp", "Plays a series of system beeps through your speakers. Loud enough to grab your attention if you're tabbed away.");
        public static readonly LocString BeepCount = new("safety.gm.beepCount", "Beep count");
        public static readonly LocString BeepCountHelp = new("safety.gm.beepCountHelp", "How many beeps to play in the burst.");
        public static readonly LocString BeepCountFormat = new("safety.gm.beepCountFormat", "%d beeps");
        public static readonly LocString BeepLength = new("safety.gm.beepLength", "Beep length");
        public static readonly LocString BeepLengthHelp = new("safety.gm.beepLengthHelp", "How long each beep lasts.");
        public static readonly LocString BeepLengthFormat = new("safety.gm.beepLengthFormat", "%d ms each");
        public static readonly LocString BeepPitch = new("safety.gm.beepPitch", "Beep pitch");
        public static readonly LocString BeepPitchHelp = new("safety.gm.beepPitchHelp", "Tone frequency of each beep.");
        public static readonly LocString BeepPitchFormat = new("safety.gm.beepPitchFormat", "%d Hz");
        public static readonly LocString GmActions = new("safety.gm.actions", "Actions");
        public static readonly LocString GmCommands = new("safety.gm.commands", "Custom commands");
        public static readonly LocString GmCommandsHelp = new("safety.gm.commandsHelp", "Chat commands to run when a GM is spotted. Useful for things like /logout, /sh stay calm, or a macro.");
        public static readonly LocString GmKill = new("safety.gm.kill", "Kill the game");
        public static readonly LocString GmKillHelp = new("safety.gm.killHelp", "Hard-terminate the game process via /xlkill. The last-resort option; no goodbyes, no cutscene, no logout. You'll get a disconnect.");
        public static readonly LocString NoCommands = new("safety.gm.noCommands", "No commands queued.");
    }
}
