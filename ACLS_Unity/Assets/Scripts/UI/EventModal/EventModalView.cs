using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ACLS.Authoring;
using ACLS.Data;
using ACLS.Loc;
using ACLS.Sim;

namespace ACLS.UI
{
    public sealed class EventModalView : MonoBehaviour
    {
        private World world;
        private GameObject root;
        private TextMeshProUGUI titleText;
        private TextMeshProUGUI descText;
        private RectTransform choicesBox;

        public void Bind(World world)
        {
            this.world = world;
            BuildUi();
            transform.GetChild(0).gameObject.SetActive(false);
            root.SetActive(false);

            world.OnEventQueued += OnEventQueued;
            world.OnPlayerSwitched += ShowInheritance;
            world.OnGameOver += ShowGameOver;
        }

        private void OnEventQueued(PendingEvent _) => RefreshFromQueue();

        private void OnDestroy()
        {
            if (world != null)
            {
                world.OnEventQueued -= OnEventQueued;
                world.OnPlayerSwitched -= ShowInheritance;
                world.OnGameOver -= ShowGameOver;
            }
        }

        private void Update()
        {
            if (world == null || root.activeSelf) return;
            if (world.EventQueue.Count > 0) RefreshFromQueue();
        }

        private void BuildUi()
        {
            var dim = UiKit.CreatePanel(transform, "Dim", Vector2.zero, Vector2.one,
                new Color(0, 0, 0, 0.55f));

            root = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            root.transform.SetParent(transform, false);
            var rt = (RectTransform)root.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(720, 420);
            root.GetComponent<Image>().color = new Color(0.13f, 0.13f, 0.17f, 0.97f);

            titleText = UiKit.CreateText(root.transform, "Title", 31, TextAlignmentOptions.Center);
            var titleRt = (RectTransform)titleText.transform;
            titleRt.anchorMin = new Vector2(0, 1);
            titleRt.anchorMax = new Vector2(1, 1);
            titleRt.pivot = new Vector2(0.5f, 1);
            titleRt.sizeDelta = new Vector2(0, 50);
            titleRt.anchoredPosition = new Vector2(0, -8);

            descText = UiKit.CreateText(root.transform, "Desc", 21, TextAlignmentOptions.TopLeft);
            var descRt = (RectTransform)descText.transform;
            descRt.anchorMin = new Vector2(0, 0.4f);
            descRt.anchorMax = new Vector2(1, 1);
            descRt.offsetMin = new Vector2(28, 0);
            descRt.offsetMax = new Vector2(-28, -64);

            var choicesGo = new GameObject("Choices", typeof(RectTransform), typeof(VerticalLayoutGroup));
            choicesGo.transform.SetParent(root.transform, false);
            choicesBox = (RectTransform)choicesGo.transform;
            choicesBox.anchorMin = new Vector2(0, 0);
            choicesBox.anchorMax = new Vector2(1, 0.4f);
            choicesBox.offsetMin = new Vector2(28, 16);
            choicesBox.offsetMax = new Vector2(-28, -8);
            var vlg = choicesGo.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 6;
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
        }

        private void SetVisible(bool v)
        {
            transform.GetChild(0).gameObject.SetActive(v);
            root.SetActive(v);
            if (v) world.Paused = true;
            else world.Paused = false;
        }

        private void ClearChoices()
        {
            for (int i = choicesBox.childCount - 1; i >= 0; i--)
                Destroy(choicesBox.GetChild(i).gameObject);
        }

        private RectTransform AddChoiceButton(string label, System.Action onClick)
        {
            var btn = UiKit.CreateButton(choicesBox, "Choice", label, onClick);
            var rt = (RectTransform)btn.transform;
            var le = btn.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 36;
            le.preferredHeight = 36;
            return rt;
        }

        private void RefreshFromQueue()
        {
            if (root.activeSelf) return;
            if (world.EventQueue.Count == 0) return;
            var pe = world.EventQueue[0];
            var def = Registry.GetEvent(pe.EventDefId);
            var actor = world.GetCharacter(pe.ActorCharacterId);
            if (def == null || actor == null)
            {
                world.EventQueue.RemoveAt(0);
                return;
            }
            ShowEvent(def, pe, actor);
        }

        private (string name, string value)[] ActorArgs(Character actor) => new[]
        {
            ("actor", actor.Name),
            ("age", actor.AgeAt(world.Date).ToString()),
        };

        private void ShowEvent(GameEventDef def, PendingEvent pe, Character actor)
        {
            ClearChoices();
            titleText.text = L10n.T(def.TitleKey);
            descText.text = L10n.T(def.DescriptionKey, ActorArgs(actor));

            var visibleChoices = new List<ChoiceDef>();
            for (int i = 0; i < def.Choices.Length; i++)
            {
                var c = def.Choices[i];
                if (Conditions.EvalAll(c.VisibleIf, world, actor))
                    visibleChoices.Add(c);
            }
            if (visibleChoices.Count == 0)
            {
                AddChoiceButton("(继续)", () => DismissEvent());
            }
            else
            {
                foreach (var choice in visibleChoices)
                {
                    var cap = choice;
                    AddChoiceButton(L10n.T(cap.TextKey), () => OnChoicePicked(def, pe, actor, cap));
                }
            }
            SetVisible(true);
        }

        private void OnChoicePicked(GameEventDef def, PendingEvent pe, Character actor, ChoiceDef choice)
        {
            Effects.ApplyAll(choice.Effects, world, actor);

            if (!string.IsNullOrEmpty(choice.ResultKey))
            {
                ClearChoices();
                descText.text = L10n.T(choice.ResultKey, ActorArgs(actor));
                AddChoiceButton("(继续)", () => DismissEvent());
            }
            else
            {
                DismissEvent();
            }
        }

        private void DismissEvent()
        {
            if (world.EventQueue.Count > 0) world.EventQueue.RemoveAt(0);
            SetVisible(false);
        }

        private void ShowInheritance(Character deceased, Character heir)
        {
            ClearChoices();
            titleText.text = L10n.T("inheritance.title");
            descText.text = L10n.T("inheritance.desc",
                ("deceased", deceased?.Name ?? "?"),
                ("heir", heir?.Name ?? "?"));
            AddChoiceButton(L10n.T("inheritance.choice"), () => SetVisible(false));
            SetVisible(true);
        }

        private void ShowGameOver(Character deceased)
        {
            ClearChoices();
            titleText.text = L10n.T("gameover.title");
            descText.text = L10n.T("gameover.desc", ("deceased", deceased?.Name ?? "?"));
            AddChoiceButton(L10n.T("gameover.choice"), () => { /* stay visible */ });
            SetVisible(true);
            world.Paused = true;
        }
    }
}
