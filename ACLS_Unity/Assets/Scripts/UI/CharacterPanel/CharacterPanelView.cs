using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ACLS.Authoring;
using ACLS.Loc;
using ACLS.Sim;

namespace ACLS.UI
{
    public sealed class CharacterPanelView : MonoBehaviour
    {
        private World world;
        private TextMeshProUGUI content;

        public void Bind(World world)
        {
            this.world = world;
            UiKit.CreatePanel(transform, "Bg",
                Vector2.zero, Vector2.one, new Color(0.10f, 0.10f, 0.14f, 0.85f))
                .transform.SetAsFirstSibling();

            content = UiKit.CreateText(transform, "Content", 18, TextAlignmentOptions.TopLeft);
            var rt = (RectTransform)content.transform;
            rt.offsetMin = new Vector2(20, 20);
            rt.offsetMax = new Vector2(-20, -20);

            world.OnMonthTick += Refresh;
            world.OnPlayerSwitched += OnPlayerSwitched;
            world.OnPlayerSet += Refresh;
            Refresh();
        }

        private void OnPlayerSwitched(Character _, Character __) => Refresh();

        private void OnDestroy()
        {
            if (world != null)
            {
                world.OnMonthTick -= Refresh;
                world.OnPlayerSwitched -= OnPlayerSwitched;
                world.OnPlayerSet -= Refresh;
            }
        }

        private void Refresh()
        {
            if (content == null) return;
            var p = world.Player;
            if (p == null) { content.text = "(无玩家角色)"; return; }

            var sb = new StringBuilder();
            sb.Append("<size=28><b>").Append(p.Name);
            if (!string.IsNullOrEmpty(p.Courtesy)) sb.Append(" ").Append(p.Courtesy);
            sb.Append("</b></size>\n");
            sb.Append(p.AgeAt(world.Date)).Append(" ").Append(L10n.T("char.panel.age"));

            int titleId = p.Identity?.TitleId ?? 0;
            sb.Append("    ").Append(titleId == 0 ? L10n.T("title.unranked") : ("title." + titleId));

            int locId = p.Identity?.LocationId ?? 0;
            var loc = world.GetLocation(locId);
            if (loc != null) sb.Append("    ").Append(loc.Name);
            sb.Append("\n\n");

            sb.AppendLine($"武 {p.Stats.Wu,3}    统 {p.Stats.Tong,3}");
            sb.AppendLine($"智 {p.Stats.Zhi,3}    政 {p.Stats.Zheng,3}");
            sb.AppendLine($"魅 {p.Stats.Mei,3}");
            sb.AppendLine();

            sb.Append(L10n.T("char.panel.traits"));
            if (p.Traits.Count == 0) sb.Append("（无）");
            else
            {
                for (int i = 0; i < p.Traits.Count; i++)
                {
                    if (i > 0) sb.Append("、");
                    var def = Registry.GetTrait(p.Traits[i]);
                    sb.Append(def != null ? L10n.T(def.DisplayNameKey) : ("#" + p.Traits[i]));
                }
            }
            sb.AppendLine();
            sb.Append("\n钱：").Append(world.Gold);

            var spouse = world.GetCharacter(p.SpouseId);
            if (spouse != null) sb.Append("\n").Append(L10n.T("char.panel.spouse")).Append("：").Append(spouse.Name).Append(spouse.IsDead ? "（已故）" : "");
            if (p.ChildrenIds.Count > 0)
            {
                sb.Append("\n").Append(L10n.T("char.panel.children")).Append("：");
                for (int i = 0; i < p.ChildrenIds.Count; i++)
                {
                    if (i > 0) sb.Append("、");
                    var c = world.GetCharacter(p.ChildrenIds[i]);
                    if (c == null) continue;
                    sb.Append(c.Name).Append(c.IsDead ? "（殁）" : $"（{c.AgeAt(world.Date)}）");
                }
            }

            content.text = sb.ToString();
        }
    }
}
