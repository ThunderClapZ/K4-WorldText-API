using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using K4WorldTextSharedAPI;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using static CounterStrikeSharp.API.Core.Listeners;

namespace K4ryuuCS2WorldTextAPI
{
	[MinimumApiVersion(227)]
	public class Plugin : BasePlugin
	{
		public override string ModuleName => "CS2 WorldText API";
		public override string ModuleVersion => "1.2.3";
		public override string ModuleAuthor => "K4ryuu";

		public static PluginCapability<IK4WorldTextSharedAPI> Capability_SharedAPI { get; } = new("k4-worldtext:sharedapi");

		public string configFilePath = string.Empty;
		public List<WorldTextConfig>? loadedConfigs = null;
		public List<MultilineWorldText> multilineWorldTexts = new List<MultilineWorldText>();

		public override void Load(bool hotReload)
		{
			Capabilities.RegisterPluginCapability(Capability_SharedAPI, () => new GameTextAPIHandler(this));

			RegisterEventHandler((EventRoundStart @event, GameEventInfo info) =>
			{
				if (loadedConfigs is null)
					LoadConfig(Server.MapName);

				multilineWorldTexts.ForEach(multilineWorldText => multilineWorldText.Update());
				return HookResult.Continue;
			});

			RegisterListener<Listeners.OnMapEnd>(() =>
			{
				loadedConfigs?.Clear();
				loadedConfigs = null;
				multilineWorldTexts.Clear();
			});

			RegisterListener<OnMapStart>(OnMapStartHandler);

			if (hotReload)
				LoadConfig(Server.MapName);
		}

		private void OnMapStartHandler(string mapName)
		{
			configFilePath = Path.Combine(ModuleDirectory, $"worldtext_{mapName}.json");
            if (!File.Exists(configFilePath))
            {
                File.WriteAllText(configFilePath, "[]");
            }
		}

		public override void Unload(bool hotReload)
		{
			multilineWorldTexts.ForEach(multilineWorldText => multilineWorldText.Dispose());
		}

		public class GameTextAPIHandler : IK4WorldTextSharedAPI
		{
			public Plugin plugin;
			public GameTextAPIHandler(Plugin plugin)
			{
				this.plugin = plugin;
			}

			public int AddWorldText(TextPlacement placement, TextLine textLine, Vector position, QAngle angle, bool saveConfig = false)
			{
				return this.AddWorldText(placement, new List<TextLine> { textLine }, position, angle);
			}

			public int AddWorldText(TextPlacement placement, List<TextLine> textLines, Vector position, QAngle angle, bool saveConfig = false)
			{
				MultilineWorldText multilineWorldText = new MultilineWorldText(plugin, textLines, saveConfig);
				multilineWorldText.Spawn(position, angle, placement);

				plugin.multilineWorldTexts.Add(multilineWorldText);
				return multilineWorldText.Id;
			}

			public int AddWorldTextAtPlayer(CCSPlayerController player, TextPlacement placement, TextLine textLine, bool saveConfig = false)
			{
				return this.AddWorldTextAtPlayer(player, placement, new List<TextLine> { textLine });
			}

			public int AddWorldTextAtPlayer(CCSPlayerController player, TextPlacement placement, List<TextLine> textLines, bool saveConfig = false)
			{
				return plugin.SpawnMultipleLines(player, placement, textLines, saveConfig);
			}

			public void UpdateWorldText(int id, TextLine? textLine = null)
			{
				this.UpdateWorldText(id, textLine is null ? null : new List<TextLine> { textLine });
			}

			public void UpdateWorldText(int id, List<TextLine>? textLines = null)
			{
				MultilineWorldText? target = plugin.multilineWorldTexts.Find(wt => wt.Id == id);
				if (target is null)
					throw new Exception($"WorldText with ID {id} not found.");

				target.Update(textLines);
			}

			public void RemoveWorldText(int id, bool removeFromConfig = true)
			{
				MultilineWorldText? target = plugin.multilineWorldTexts.Find(wt => wt.Id == id);
				if (target is null)
					throw new Exception($"WorldText with ID {id} not found.");

				target.Dispose();
				plugin.multilineWorldTexts.Remove(target);

				if (removeFromConfig)
				{
					plugin.loadedConfigs?.RemoveAll(config => config.Lines == target.Lines && config.AbsOrigin == target.Texts[0].AbsOrigin.ToString() && config.AbsRotation == target.Texts[0].AbsRotation.ToString());
					plugin.SaveConfig();
				}
			}

			public List<CPointWorldText>? GetWorldTextLineEntities(int id)
			{
				MultilineWorldText? target = plugin.multilineWorldTexts.Find(wt => wt.Id == id);
				if (target is null)
					throw new Exception($"WorldText with ID {id} not found.");

				return target.Texts.Where(t => t.Entity != null).Select(t => t.Entity).Cast<CPointWorldText>().ToList();
			}

			public void TeleportWorldText(int id, Vector position, QAngle angle, bool modifyConfig = false)
			{
				MultilineWorldText? target = plugin.multilineWorldTexts.Find(wt => wt.Id == id);
				if (target is null)
					throw new Exception($"WorldText with ID {id} not found.");

				target.Teleport(position, angle, modifyConfig);
			}

			public void RemoveAllTemporary()
			{
				plugin.multilineWorldTexts.Where(wt => !wt.SaveToConfig).ToList().ForEach(multilineWorldText => multilineWorldText.Dispose());
			}
		}

		public void LoadConfig(string mapName)
		{
			configFilePath = Path.Combine(ModuleDirectory, $"worldtext_{mapName}.json");

			if (!File.Exists(configFilePath))
				return;

			try
			{
				string json = File.ReadAllText(configFilePath);
				loadedConfigs = JsonConvert.DeserializeObject<List<WorldTextConfig>>(json);

				if (loadedConfigs == null)
				{
					Logger.LogWarning($"Failed to deserialize configuration file: {configFilePath}");
					loadedConfigs = new List<WorldTextConfig>();
				}

				foreach (var config in loadedConfigs)
				{
					Vector vector = ParseVector(config.AbsOrigin);
					QAngle qAngle = ParseQAngle(config.AbsRotation);

					MultilineWorldText multilineWorldText = new MultilineWorldText(this, config.Lines, fromConfig: true);
					multilineWorldText.Spawn(vector, qAngle, placement: config.Placement);

					multilineWorldTexts.Add(multilineWorldText);
				}
			}
			catch (Exception ex)
			{
				Logger.LogError($"Error while loading configuration file: {ex.Message}");
			}
		}

		public void SaveConfig()
		{
			string updatedJson = JsonConvert.SerializeObject(loadedConfigs, Formatting.Indented);
			File.WriteAllText(configFilePath, updatedJson);
		}

		[ConsoleCommand("css_wt", "Spawns a world text")]
		[ConsoleCommand("css_worldtext", "Spawns a world text")]
		[RequiresPermissions("@css/ban")]
		public void OnWorldTextSpawn(CCSPlayerController player, CommandInfo command)
		{
			if (command.ArgCount < 2)
			{
				command.ReplyToCommand($" {ChatColors.Silver}[ {ChatColors.Lime}地图文本 {ChatColors.Silver}] {ChatColors.Red}用法: {ChatColors.Yellow}!wt <floor|wall>");
				return;
			}

			if (player.PlayerPawn.Value?.Health <= 0)
			{
				command.ReplyToCommand($" {ChatColors.Silver}[ {ChatColors.Lime}地图文本 {ChatColors.Silver}] {ChatColors.Red}活着才能用.");
				return;
			}

			string placementName = command.GetCommandString.Split(' ')[1];

			TextPlacement? placement;
			switch (placementName.ToLower())
			{
				case "floor":
					placement = TextPlacement.Floor;
					break;
				case "wall":
					placement = TextPlacement.Wall;
					break;
				default:
					command.ReplyToCommand($" {ChatColors.Silver}[ {ChatColors.Lime}地图文本 {ChatColors.Silver}] {ChatColors.Red}无效的参数. {ChatColors.Yellow}使用 'floor' 或 'wall'.");
					return;
			}

			List<TextLine> startLines = new List<TextLine>
			{
				new TextLine
				{
					Text = "这是一行文本!",
					Color = Color.Yellow,
					FontSize = 24
				},
				new TextLine
				{
					Text = "可以在配置文件中修改文本的内容.",
					Color = Color.Cyan,
					FontSize = 18
				},
				new TextLine
				{
					Text = new Random().Next().ToString(),
					Color = Color.Red,
					FontSize = 20
				}
			};

			SpawnMultipleLines(player, (TextPlacement)placement, startLines, true);
			command.ReplyToCommand($" {ChatColors.Silver}[ {ChatColors.Lime}地图文本 {ChatColors.Silver}] {ChatColors.Green}生成成功,请修改配置文件更改文本内容.");
		}

		[ConsoleCommand("css_wtpreset", "Spawns a world text")]
		[RequiresPermissions("@css/ban")]
		public void OnWorldTextSpawnPreset(CCSPlayerController player, CommandInfo command)
		{
			if (command.ArgCount < 4)
			{
				command.ReplyToCommand($" {ChatColors.Silver}[ {ChatColors.Lime}地图文本 {ChatColors.Silver}] {ChatColors.Red}用法: {ChatColors.Yellow}!wtpreset <floor|wall> <presetname> <fontsize>");
				return;
			}

			if (player.PlayerPawn.Value?.Health <= 0)
			{
				command.ReplyToCommand($" {ChatColors.Silver}[ {ChatColors.Lime}地图文本 {ChatColors.Silver}] {ChatColors.Red}活着才能用.");
				return;
			}

			string placementName = command.GetCommandString.Split(' ')[1];

			TextPlacement? placement;
			switch (placementName.ToLower())
			{
				case "floor":
					placement = TextPlacement.Floor;
					break;
				case "wall":
					placement = TextPlacement.Wall;
					break;
				default:
					command.ReplyToCommand($" {ChatColors.Silver}[ {ChatColors.Lime}地图文本 {ChatColors.Silver}] {ChatColors.Red}无效的参数. {ChatColors.Yellow}使用 'floor' 或 'wall'.");
					return;
			}

			// 设置预设
			string presetName = command.GetCommandString.Split(' ')[2];
			string presetFontSizeString = command.GetCommandString.Split(' ')[3];
			int presetFontSize = int.Parse(presetFontSizeString);
			string wtTextL1 = "这是一行文本";
			string wtTextL2 = "这是一行文本";
			string wtTextL3 = "这是一行文本";
			Color wtColorL1 = Color.Yellow;
			Color wtColorL2 = Color.Cyan;
			Color wtColorL3 = Color.Red;
			int wtSizeL1 = presetFontSize;
			int wtSizeL2 = presetFontSize;
			int wtSizeL3 = presetFontSize;
			switch (presetName.ToLower())
			{
				case "武器库":
					wtTextL1 = "";
					wtTextL2 = "武器库";
					wtTextL3 = "";
					wtColorL1 = Color.Blue;
					wtColorL2 = Color.Blue;
					wtColorL3 = Color.Blue;
					break;
				case "密道":
					wtTextL1 = "";
					wtTextL2 = "密道";
					wtTextL3 = "";
					wtColorL1 = Color.Red;
					wtColorL2 = Color.Red;
					wtColorL3 = Color.Red;
					break;
				case "未知":
					wtTextL1 = "未知区域";
					wtTextL2 = "?";
					wtTextL3 = "未知区域";
					wtColorL1 = Color.Yellow;
					wtColorL2 = Color.Yellow;
					wtColorL3 = Color.Yellow;
					break;
				case "ct房":
					wtTextL1 = "";
					wtTextL2 = "CT房";
					wtTextL3 = "该区域自由后狱警禁止进入";
					wtColorL1 = Color.Blue;
					wtColorL2 = Color.Blue;
					wtColorL3 = Color.Blue;
					break;
				case "规则":
					wtTextL1 = "当狱警请务必知晓规则";
					wtTextL2 = "请在规定时间内离开武器库";
					wtTextL3 = "狱警不能进密道和捡地图武器";
					wtColorL1 = Color.Orange;
					wtColorL2 = Color.Orange;
					wtColorL3 = Color.Orange;
					break;
				case "bug":
					wtTextL1 = "BUG";
					wtTextL2 = "这里是BUG区域";
					wtTextL3 = "进入会导致被服务器封禁";
					wtColorL1 = Color.Yellow;
					wtColorL2 = Color.Yellow;
					wtColorL3 = Color.Yellow;
					break;
				case "警戒":
					wtTextL1 = "警戒区域";
					wtTextL2 = "请勿跨越区域或出图";
					wtTextL3 = "越过此区域可能会导致被服务器封禁";
					wtColorL1 = Color.Yellow;
					wtColorL2 = Color.Yellow;
					wtColorL3 = Color.Yellow;
					break;
			}

			// 文本
			List<TextLine> startLines = new List<TextLine>
			{

				new TextLine
				{
					Text = wtTextL1,
					Color = wtColorL1,
					FontSize = wtSizeL1
				},
				new TextLine
				{
					Text = wtTextL2,
					Color = wtColorL2,
					FontSize = wtSizeL2
				},
				new TextLine
				{
					Text = wtTextL3,
					Color = wtColorL3,
					FontSize = wtSizeL3
				}
			};

			SpawnMultipleLines(player, (TextPlacement)placement, startLines, true);
			command.ReplyToCommand($" {ChatColors.Silver}[ {ChatColors.Lime}地图文本 {ChatColors.Silver}] {ChatColors.Green}预设{presetName}生成成功.文本大小{presetFontSize}");
		}

		[ConsoleCommand("css_rwt", "Removes a world text")]
		[ConsoleCommand("css_removeworldtext", "Removes a world text")]
		[RequiresPermissions("@css/ban")]
		public void OnRemoveWorldTextSpawn(CCSPlayerController player, CommandInfo command)
		{
			if (player.PlayerPawn.Value?.Health <= 0)
			{
				command.ReplyToCommand($" {ChatColors.Silver}[ {ChatColors.Lime}地图文本 {ChatColors.Silver}] {ChatColors.Red}活着才能用.");
				return;
			}

			var GameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;

			if (GameRules is null)
				return;

			MultilineWorldText? target = multilineWorldTexts
				.Where(multilineWorldText => DistanceTo(multilineWorldText.Texts[0].AbsOrigin, player.PlayerPawn.Value!.AbsOrigin!) < 100)
				.OrderBy(multilineWorldText => DistanceTo(multilineWorldText.Texts[0].AbsOrigin, player.PlayerPawn.Value!.AbsOrigin!))
				.FirstOrDefault();

			if (target is null)
			{
				command.ReplyToCommand($" {ChatColors.Silver}[ {ChatColors.Lime}地图文本 {ChatColors.Silver}] {ChatColors.Red}靠近你想要删除的文本才可以删除.");
				return;
			}

			target.Dispose();
			multilineWorldTexts.Remove(target);

			loadedConfigs?.RemoveAll(config => config.Lines == target.Lines && config.AbsOrigin == target.Texts[0].AbsOrigin.ToString() && config.AbsRotation == target.Texts[0].AbsRotation.ToString());
			SaveConfig();

			command.ReplyToCommand($" {ChatColors.Silver}[ {ChatColors.Lime}地图文本 {ChatColors.Silver}] {ChatColors.Green}文本已删除!");
		}

		[ConsoleCommand("css_wt_reload", "重载配置文件")]
		[RequiresPermissions("@css/ban")]
		public void OnWorldTextReload(CCSPlayerController player, CommandInfo command)
		{
			multilineWorldTexts.ForEach(multilineWorldText => multilineWorldText.Dispose());
			multilineWorldTexts.Clear();

			loadedConfigs?.Clear();
			LoadConfig(Server.MapName);

			command.ReplyToCommand($" {ChatColors.Silver}[ {ChatColors.Lime}地图文本 {ChatColors.Silver}] {ChatColors.Green}重载成功!");
		}

		[ConsoleCommand("css_wti", "Shows informations about nearest world text")]
		[RequiresPermissions("@css/root")]
		public void OnWorldTextInfo(CCSPlayerController player, CommandInfo command)
		{
			if (player.PlayerPawn.Value?.Health <= 0)
			{
				command.ReplyToCommand($" {ChatColors.Silver}[ {ChatColors.Lime}地图文本 {ChatColors.Silver}] {ChatColors.Red}活着才能使用.");
				return;
			}

			MultilineWorldText? target = multilineWorldTexts
				.Where(multilineWorldText => DistanceTo(multilineWorldText.Texts[0].AbsOrigin, player.PlayerPawn.Value!.AbsOrigin!) < 100)
				.OrderBy(multilineWorldText => DistanceTo(multilineWorldText.Texts[0].AbsOrigin, player.PlayerPawn.Value!.AbsOrigin!))
				.FirstOrDefault();

			if (target is null)
			{
				command.ReplyToCommand($" {ChatColors.Silver}[ {ChatColors.Lime}地图文本 {ChatColors.Silver}] {ChatColors.Red}靠近文本才能查看信息.");
				return;
			}

			player.PrintToChat($" {ChatColors.Silver}[ {ChatColors.Lime}地图文本 {ChatColors.Silver}] {ChatColors.Green}文本信息");
			player.PrintToChat($" {ChatColors.Silver}Placement: {ChatColors.Yellow}{target.placement switch { TextPlacement.Floor => "Floor", TextPlacement.Wall => "Wall", _ => "Unknown" }}");
			player.PrintToChat($" {ChatColors.Silver}Lines: {ChatColors.Yellow}{target.Texts.Count}");
			player.PrintToChat($" {ChatColors.Silver}Location: {ChatColors.Yellow}{target.Texts[0].AbsOrigin}");
			player.PrintToChat($" {ChatColors.Silver}Rotation: {ChatColors.Yellow}{target.Texts[0].AbsRotation}");
			player.PrintToChat($" {ChatColors.Silver}Saved in config: {ChatColors.Yellow}{(loadedConfigs?.Any(config => config.Lines == target.Lines && config.AbsOrigin == target.Texts[0].AbsOrigin.ToString() && config.AbsRotation == target.Texts[0].AbsRotation.ToString()) ?? false ? "Yes" : "No")}");
		}

		public int SpawnMultipleLines(CCSPlayerController player, TextPlacement placement, List<TextLine> lines, bool saveConfig = false)
		{
			Vector AbsOrigin = Vector.Zero;
			QAngle AbsRotation = QAngle.Zero;
			QAngle tempRotation = GetNormalizedAngles(player);
			switch (placement)
			{
				case TextPlacement.Wall:
					AbsOrigin = GetEyePosition(player, lines);
					AbsRotation = new QAngle(tempRotation.X, tempRotation.Y + 270, tempRotation.Z + 90);
					break;
				case TextPlacement.Floor:
					AbsOrigin = player.PlayerPawn.Value!.AbsOrigin!.With(z: player.PlayerPawn.Value!.AbsOrigin!.Z + 1);
					AbsRotation = new QAngle(tempRotation.X, tempRotation.Y + 270, tempRotation.Z);
					break;
			}

			string direction = EntityFaceToDirection(player.PlayerPawn.Value!.AbsRotation!.Y);
			Vector offset = GetDirectionOffset(direction, 15);

			MultilineWorldText multilineWorldText = new MultilineWorldText(this, lines, saveConfig);
			multilineWorldText.Spawn(AbsOrigin + offset, AbsRotation, placement);

			multilineWorldTexts.Add(multilineWorldText);
			return multilineWorldText.Id;
		}

		public static QAngle GetNormalizedAngles(CCSPlayerController player)
		{
			QAngle AbsRotation = player.PlayerPawn.Value!.AbsRotation!;
			return new QAngle(
				AbsRotation.X,
				(float)Math.Round(AbsRotation.Y / 10.0) * 10,
				AbsRotation.Z
			);
		}

		public static Vector GetEyePosition(CCSPlayerController player, List<TextLine> lines)
		{
			Vector absorigin = player.PlayerPawn.Value!.AbsOrigin!;
			CPlayer_CameraServices camera = player.PlayerPawn.Value!.CameraServices!;

			float totalHeight = lines.Sum(line => line.FontSize / 5);
			return new Vector(absorigin.X, absorigin.Y, absorigin.Z + camera.OldPlayerViewOffsetZ + totalHeight);
		}

		public string EntityFaceToDirection(float yaw)
		{
			if (yaw >= -22.5 && yaw < 22.5)
				return "X";
			else if (yaw >= 22.5 && yaw < 67.5)
				return "XY";
			else if (yaw >= 67.5 && yaw < 112.5)
				return "Y";
			else if (yaw >= 112.5 && yaw < 157.5)
				return "-XY";
			else if (yaw >= 157.5 || yaw < -157.5)
				return "-X";
			else if (yaw >= -157.5 && yaw < -112.5)
				return "-X-Y";
			else if (yaw >= -112.5 && yaw < -67.5)
				return "-Y";
			else
				return "X-Y";
		}

		public Vector GetDirectionOffset(string direction, float offsetValue)
		{
			return direction switch
			{
				"X" => new Vector(offsetValue, 0, 0),
				"-X" => new Vector(-offsetValue, 0, 0),
				"Y" => new Vector(0, offsetValue, 0),
				"-Y" => new Vector(0, -offsetValue, 0),
				"XY" => new Vector(offsetValue, offsetValue, 0),
				"-XY" => new Vector(-offsetValue, offsetValue, 0),
				"X-Y" => new Vector(offsetValue, -offsetValue, 0),
				"-X-Y" => new Vector(-offsetValue, -offsetValue, 0),
				_ => Vector.Zero
			};
		}

		public static Vector ParseVector(string vectorString)
		{
			string[] components = vectorString.Split(' ');
			if (components.Length != 3)
				throw new ArgumentException("Invalid vector string format.");

			float x = float.Parse(components[0]);
			float y = float.Parse(components[1]);
			float z = float.Parse(components[2]);

			return new Vector(x, y, z);
		}

		public static QAngle ParseQAngle(string qangleString)
		{
			string[] components = qangleString.Split(' ');
			if (components.Length != 3)
				throw new ArgumentException("Invalid QAngle string format.");

			float x = float.Parse(components[0]);
			float y = float.Parse(components[1]);
			float z = float.Parse(components[2]);

			return new QAngle(x, y, z);
		}

		private float DistanceTo(Vector a, Vector b)
		{
			return (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));
		}

		public class WorldTextConfig
		{
			public TextPlacement Placement { get; set; }
			public required List<TextLine> Lines { get; set; }
			public required string AbsOrigin { get; set; }
			public required string AbsRotation { get; set; }
		}
	}
}
