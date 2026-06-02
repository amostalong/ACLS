using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ACLS.Authoring;
using ACLS.Llm;
using ACLS.Sim;

namespace ACLS.UI
{
    public sealed class AvatarBarView : MonoBehaviour
    {
        private const float TileWidth = 64f;
        private const float TileHeight = 72f;

        private World world;
        private ChatBridge bridge;
        private RectTransform tilesContainer;

        public void Bind(World world, ChatBridge bridge)
        {
            this.world = world;
            this.bridge = bridge;

            BuildShell();

            if (bridge != null) bridge.OnParticipantsChanged += OnParticipantsChanged;
            world.OnMonthTick += Refresh;
            world.OnPlayerSwitched += OnPlayerSwitched;
            world.OnPlayerSet += Refresh;

            Refresh();
        }

        private void OnDestroy()
        {
            if (bridge != null) bridge.OnParticipantsChanged -= OnParticipantsChanged;
            if (world != null)
            {
                world.OnMonthTick -= Refresh;
                world.OnPlayerSwitched -= OnPlayerSwitched;
                world.OnPlayerSet -= Refresh;
            }
        }

        private void OnParticipantsChanged(IReadOnlyList<LlmReply.Participant> _) => Refresh();
        private void OnPlayerSwitched(Character _, Character __) => Refresh();

        private void BuildShell()
        {
            UiKit.CreatePanel(transform, "Bg",
                Vector2.zero, Vector2.one, new Color(0.07f, 0.07f, 0.10f, 0.95f))
                .transform.SetAsFirstSibling();

            var tilesGo = new GameObject("Tiles",
                typeof(RectTransform),
                typeof(HorizontalLayoutGroup));
            tilesGo.transform.SetParent(transform, false);
            tilesContainer = (RectTransform)tilesGo.transform;
            tilesContainer.anchorMin = Vector2.zero;
            tilesContainer.anchorMax = Vector2.one;
            tilesContainer.offsetMin = new Vector2(12, 8);
            tilesContainer.offsetMax = new Vector2(-12, -8);

            var hlg = tilesGo.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10;
            hlg.padding = new RectOffset(0, 0, 0, 0);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = false;
            hlg.childControlHeight = false;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
        }

        private void Refresh()
        {
            if (tilesContainer == null) return;

            for (int i = tilesContainer.childCount - 1; i >= 0; i--)
                Destroy(tilesContainer.GetChild(i).gameObject);

            var participants = bridge?.CurrentParticipants;
            if (participants != null && participants.Count > 0)
            {
                RenderFromParticipants(participants);
            }
            else
            {
                RenderFromFamily();
            }
        }

        private void RenderFromParticipants(IReadOnlyList<LlmReply.Participant> participants)
        {
            int playerId = world.PlayerCharacterId;
            for (int i = 0; i < participants.Count; i++)
            {
                var p = participants[i];
                var ch = ResolveCharacter(p.Name);
                bool isPlayer = ch != null && ch.Id == playerId;
                if (ch != null)
                {
                    CreateTile(LastChar(ch.Name), CompactRole(p.Role, ch, isPlayer),
                               ch.AgeAt(world.Date), ColorForRole(p.Role, isPlayer), isPlayer);
                }
                else
                {
                    CreateTile(LastChar(p.Name), p.Role, age: -1,
                               ColorForRole(p.Role, isPlayer: false), isPlayer: false);
                }
            }
        }

        private void RenderFromFamily()
        {
            var p = world.Player;
            if (p == null) return;
            CreateTile(LastChar(p.Name), "你", p.AgeAt(world.Date), ColorForRole("你", true), isPlayer: true);
            var spouse = world.GetCharacter(p.SpouseId);
            if (spouse != null && spouse.IsAlive)
                CreateTile(LastChar(spouse.Name), "妻", spouse.AgeAt(world.Date), ColorForRole("妻", false), false);
            for (int i = 0; i < p.ChildrenIds.Count; i++)
            {
                var child = world.GetCharacter(p.ChildrenIds[i]);
                if (child == null || child.IsDead) continue;
                string role = child.Sex == Sex.Male ? "子" : "女";
                CreateTile(LastChar(child.Name), role, child.AgeAt(world.Date),
                           ColorForRole(role, false), false);
            }
        }

        private void CreateTile(string face, string role, int age, Color color, bool isPlayer)
        {
            var tileGo = new GameObject("Tile",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            tileGo.transform.SetParent(tilesContainer, false);

            var le = tileGo.AddComponent<LayoutElement>();
            le.preferredWidth = TileWidth;
            le.preferredHeight = TileHeight;
            le.minWidth = TileWidth;
            le.minHeight = TileHeight;

            var img = tileGo.GetComponent<Image>();
            img.color = color;

            if (isPlayer)
            {
                var outline = tileGo.AddComponent<Outline>();
                outline.effectColor = new Color(1f, 0.85f, 0.3f, 1f);
                outline.effectDistance = new Vector2(2, -2);
            }

            var faceText = UiKit.CreateText(tileGo.transform, "Face", 29, TextAlignmentOptions.Center);
            var faceRt = (RectTransform)faceText.transform;
            faceRt.anchorMin = new Vector2(0, 0.30f);
            faceRt.anchorMax = new Vector2(1, 1);
            faceRt.offsetMin = Vector2.zero;
            faceRt.offsetMax = Vector2.zero;
            faceText.text = $"<b>{face}</b>";
            faceText.color = new Color(1, 1, 1, 0.96f);

            var sub = UiKit.CreateText(tileGo.transform, "Sub", 14, TextAlignmentOptions.Center);
            var subRt = (RectTransform)sub.transform;
            subRt.anchorMin = new Vector2(0, 0);
            subRt.anchorMax = new Vector2(1, 0.30f);
            subRt.offsetMin = Vector2.zero;
            subRt.offsetMax = Vector2.zero;
            sub.text = age >= 0 ? $"{role} {age}" : role;
            sub.color = new Color(0, 0, 0, 0.78f);
        }

        private Character ResolveCharacter(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string trimmed = name.Trim();
            for (int i = 0; i < world.CharacterList.Count; i++)
            {
                var c = world.CharacterList[i];
                if (c.Name == trimmed) return c;
            }
            for (int i = 0; i < world.CharacterList.Count; i++)
            {
                var c = world.CharacterList[i];
                if (!string.IsNullOrEmpty(c.Name) && (trimmed.Contains(c.Name) || c.Name.Contains(trimmed)))
                    return c;
            }
            return null;
        }

        private static string LastChar(string s) =>
            string.IsNullOrEmpty(s) ? "?" : s.Substring(s.Length - 1);

        private static string CompactRole(string role, Character c, bool isPlayer)
        {
            if (isPlayer) return "你";
            if (string.IsNullOrEmpty(role)) return "?";
            return role.Length > 2 ? role.Substring(0, 2) : role;
        }

        private static Color ColorForRole(string role, bool isPlayer)
        {
            if (isPlayer) return new Color(0.62f, 0.50f, 0.22f, 1f);
            if (string.IsNullOrEmpty(role)) return new Color(0.32f, 0.32f, 0.36f, 1f);
            if (role.Contains("妻") || role.Contains("夫人") || role.Contains("妾")) return new Color(0.55f, 0.32f, 0.42f, 1f);
            if (role.Contains("子")) return new Color(0.32f, 0.42f, 0.62f, 1f);
            if (role.Contains("女")) return new Color(0.50f, 0.36f, 0.58f, 1f);
            if (role.Contains("父") || role.Contains("母")) return new Color(0.42f, 0.32f, 0.20f, 1f);
            if (role.Contains("友") || role.Contains("客") || role.Contains("同窗")) return new Color(0.30f, 0.50f, 0.40f, 1f);
            if (role.Contains("敌") || role.Contains("仇")) return new Color(0.55f, 0.22f, 0.22f, 1f);
            return new Color(0.32f, 0.32f, 0.36f, 1f);
        }
    }
}
