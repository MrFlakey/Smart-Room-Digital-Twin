using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SmartRoom
{
    /// <summary>Bottom-left card listing the newest entries of the room's event log; can be collapsed.</summary>
    public class EventLogPanel : MonoBehaviour
    {
        [SerializeField] RoomModel model;
        [SerializeField] RectTransform card;
        [SerializeField] GameObject list;
        [SerializeField] Button toggle;
        [SerializeField] TMP_Text toggleLabel;
        [SerializeField] TMP_Text emptyText;
        [SerializeField] GameObject[] rows;
        [SerializeField] TMP_Text[] times;
        [SerializeField] Image[] dots;
        [SerializeField] TMP_Text[] texts;
        [SerializeField] float expandedHeight = 168f;
        [SerializeField] float collapsedHeight = 42f;

        static readonly Color Red = Hex("FF5D5D");
        static readonly Color Green = Hex("7EE2A8");
        static readonly Color Yellow = Hex("FFD27A");
        static readonly Color Blue = Hex("7FD1FF");
        static readonly Color Grey = Hex("8B939E");

        bool expanded = true;

        void Awake()
        {
            toggle.onClick.AddListener(() => SetExpanded(!expanded));
            SetExpanded(true);
        }

        public void SetExpanded(bool value)
        {
            expanded = value;
            list.SetActive(value);
            toggleLabel.text = value ? "hide" : "show";
            card.sizeDelta = new Vector2(card.sizeDelta.x, value ? expandedHeight : collapsedHeight);
        }

        void Update()
        {
            if (!expanded || model == null)
                return;

            var log = model.Log;
            emptyText.gameObject.SetActive(log.Count == 0);
            for (int i = 0; i < rows.Length; i++)
            {
                bool has = i < log.Count;
                rows[i].SetActive(has);
                if (!has)
                    continue;
                var entry = log[i];
                times[i].text = entry.Time.ToString("HH:mm:ss");
                dots[i].color = KindColor(entry.Kind);
                texts[i].text = entry.Text;
            }
        }

        /// <summary>Dot colour for a log entry; also used by the History window.</summary>
        public static Color KindColor(LogKind kind)
        {
            switch (kind)
            {
                case LogKind.Fault: return Red;
                case LogKind.Recovered: return Green;
                case LogKind.Override: return Yellow;
                case LogKind.Door: return Blue;
                case LogKind.Occupancy: return Green;
                default: return Grey;
            }
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }
    }
}
