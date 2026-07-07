using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Client.View
{
    /// <summary>
    /// Pre-match screens: main menu (local / host / join), the host's waiting
    /// room, and the joining client's connection status. Built from code, same
    /// philosophy as BoardView: this class only renders and forwards clicks —
    /// GameController owns the actual LocalGameServer/NetworkHostServer/
    /// NetworkClientServer and calls back in here to update status text.
    /// </summary>
    public class LobbyView
    {
        static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.45f);
        static readonly Color FieldColor = new Color(1f, 1f, 1f, 0.10f);
        static readonly Color ErrorColor = new Color(1f, 0.55f, 0.5f);

        GameController _controller;

        GameObject _canvasRoot;
        GameObject _mainMenuPanel;
        GameObject _hostPanel;
        GameObject _joinPanel;

        InputField _nameInput;
        InputField _addressInput;
        Text _menuStatusLabel;

        Text _hostAddressLabel;
        Text _hostStatusLabel;
        Button _startMatchButton;

        Text _joinStatusLabel;

        string PlayerName => string.IsNullOrWhiteSpace(_nameInput.text) ? "Player" : _nameInput.text.Trim();

        // ------------------------------------------------------------------
        // Build (once)
        // ------------------------------------------------------------------

        public void Build(GameController controller)
        {
            _controller = controller;

            var canvasGo = new GameObject("LobbyCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasRoot = canvasGo;
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            EnsureEventSystem();

            var bg = AddRect(canvasGo.transform, "Background", Vector2.zero, Vector2.one);
            bg.gameObject.AddComponent<Image>().color = new Color(0.09f, 0.11f, 0.13f);

            BuildMainMenuPanel(canvasGo.transform);
            BuildHostPanel(canvasGo.transform);
            BuildJoinPanel(canvasGo.transform);

            ShowMainMenu();
        }

        static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
#if ENABLE_INPUT_SYSTEM
            new GameObject("EventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
#else
            new GameObject("EventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.EventSystems.StandaloneInputModule));
#endif
        }

        void BuildMainMenuPanel(Transform canvas)
        {
            _mainMenuPanel = NewPanel(canvas, "MainMenu", 560f, out var content);

            var title = AddLabel(content, "Title", 32, TextAnchor.MiddleCenter, 48);
            title.text = "Unnamed Card Game";
            title.fontStyle = FontStyle.Bold;

            AddLabel(content, "NameLabel", 18, TextAnchor.MiddleLeft, 22).text = "Your name";
            _nameInput = AddInputField(content, "NameInput", "Player", "Player", 48);

            AddSpacer(content, 6f);
            AddButton(content, "LocalButton", "Local Match (Hot-seat)", new Color(0.3f, 0.5f, 0.3f), 56,
                () => _controller.OnLocalMatchClicked());

            AddSpacer(content, 6f);
            AddButton(content, "HostButton", "Host Online Game", new Color(0.25f, 0.45f, 0.75f), 56,
                () => _controller.OnHostClicked(PlayerName));

            AddSpacer(content, 14f);
            AddLabel(content, "JoinLabel", 18, TextAnchor.MiddleLeft, 22).text = "Host address (IP:port)";
            _addressInput = AddInputField(content, "AddressInput", "127.0.0.1:7777", "", 48);
            AddButton(content, "JoinButton", "Join Online Game", new Color(0.6f, 0.45f, 0.2f), 56,
                () => _controller.OnJoinClicked(PlayerName, _addressInput.text));

            AddSpacer(content, 6f);
            _menuStatusLabel = AddLabel(content, "Status", 16, TextAnchor.MiddleCenter, 44);
            _menuStatusLabel.color = ErrorColor;

            AddLabel(content, "Hint", 13, TextAnchor.MiddleCenter, 40).text =
                "LAN play works out of the box. For the internet, the host must port-forward\n" +
                "TCP and share their public address instead of a 192.168.x.x one.";
        }

        void BuildHostPanel(Transform canvas)
        {
            _hostPanel = NewPanel(canvas, "HostLobby", 620f, out var content);

            var title = AddLabel(content, "Title", 28, TextAnchor.MiddleCenter, 40);
            title.text = "Hosting";
            title.fontStyle = FontStyle.Bold;

            _hostAddressLabel = AddLabel(content, "Address", 18, TextAnchor.MiddleCenter, 56);
            AddSpacer(content, 8f);
            _hostStatusLabel = AddLabel(content, "Status", 20, TextAnchor.MiddleCenter, 60);

            AddSpacer(content, 10f);
            _startMatchButton = AddButton(content, "StartButton", "Start Match", new Color(0.3f, 0.5f, 0.3f), 56,
                () => _controller.OnStartMatchClicked());

            AddSpacer(content, 6f);
            AddButton(content, "CancelButton", "Cancel", new Color(0.5f, 0.25f, 0.25f), 48,
                () => _controller.OnLobbyCancelClicked());
        }

        void BuildJoinPanel(Transform canvas)
        {
            _joinPanel = NewPanel(canvas, "JoinLobby", 560f, out var content);

            var title = AddLabel(content, "Title", 28, TextAnchor.MiddleCenter, 40);
            title.text = "Joining";
            title.fontStyle = FontStyle.Bold;

            _joinStatusLabel = AddLabel(content, "Status", 20, TextAnchor.MiddleCenter, 80);

            AddSpacer(content, 10f);
            AddButton(content, "CancelButton", "Cancel", new Color(0.5f, 0.25f, 0.25f), 48,
                () => _controller.OnLobbyCancelClicked());
        }

        // ------------------------------------------------------------------
        // Screen transitions (called by GameController)
        // ------------------------------------------------------------------

        public void ShowMainMenu(string status = null)
        {
            _canvasRoot.SetActive(true);   // Hide() (entering a match) may have deactivated the whole canvas
            _mainMenuPanel.SetActive(true);
            _hostPanel.SetActive(false);
            _joinPanel.SetActive(false);
            _menuStatusLabel.text = status ?? "";
        }

        public void ShowHostLobby(int port, IReadOnlyList<string> addresses)
        {
            _mainMenuPanel.SetActive(false);
            _hostPanel.SetActive(true);
            _joinPanel.SetActive(false);

            _hostAddressLabel.text = $"Share with your opponent: {string.Join("  /  ", addresses)}   (port {port})";
            SetHostOpponentStatus(false, null);
        }

        public void SetHostOpponentStatus(bool connected, string opponentName)
        {
            _hostStatusLabel.text = connected
                ? $"Opponent connected: {opponentName}\nReady when you are."
                : "Waiting for opponent to connect...";
            _startMatchButton.interactable = connected;
        }

        public void ShowJoinLobby(string address)
        {
            _mainMenuPanel.SetActive(false);
            _hostPanel.SetActive(false);
            _joinPanel.SetActive(true);
            SetJoinStatus($"Connecting to {address}...");
        }

        public void SetJoinStatus(string status) => _joinStatusLabel.text = status;

        public void Hide() => _canvasRoot.SetActive(false);

        // ------------------------------------------------------------------
        // helpers — same spirit as BoardView's, plus buttons/inputs it doesn't need
        // ------------------------------------------------------------------

        static GameObject NewPanel(Transform canvas, string name, float width, out RectTransform content)
        {
            var rect = AddRect(canvas, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            rect.sizeDelta = new Vector2(width, 900);
            rect.gameObject.AddComponent<Image>().color = PanelColor;

            var fitter = rect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(32, 32, 32, 32);
            layout.spacing = 4f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            content = rect;
            rect.gameObject.SetActive(false);
            return rect.gameObject;
        }

        static void AddSpacer(Transform parent, float height)
        {
            var rect = AddRect(parent, "Spacer", Vector2.zero, Vector2.zero);
            rect.sizeDelta = new Vector2(0, height);
            rect.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        }

        Button AddButton(Transform parent, string name, string label, Color color, float height, Action onClick)
        {
            var rect = AddRect(parent, name, Vector2.zero, Vector2.zero);
            rect.sizeDelta = new Vector2(0, height);
            rect.gameObject.AddComponent<LayoutElement>().preferredHeight = height;

            rect.gameObject.AddComponent<Image>().color = color;
            var button = rect.gameObject.AddComponent<Button>();
            button.onClick.AddListener(() => onClick());

            var label_ = AddLabel(rect, "Label", 22, TextAnchor.MiddleCenter, 0, fill: true);
            label_.text = label;
            label_.fontStyle = FontStyle.Bold;
            return button;
        }

        InputField AddInputField(Transform parent, string name, string placeholderText, string defaultText, float height)
        {
            var rect = AddRect(parent, name, Vector2.zero, Vector2.zero);
            rect.sizeDelta = new Vector2(0, height);
            rect.gameObject.AddComponent<LayoutElement>().preferredHeight = height;

            rect.gameObject.AddComponent<Image>().color = FieldColor;
            var input = rect.gameObject.AddComponent<InputField>();

            var placeholder = AddLabel(rect, "Placeholder", 20, TextAnchor.MiddleLeft, 0, fill: true);
            placeholder.text = placeholderText;
            placeholder.fontStyle = FontStyle.Italic;
            placeholder.color = new Color(1f, 1f, 1f, 0.4f);
            InsetHorizontally(placeholder.rectTransform, 12f);

            var text = AddLabel(rect, "Text", 20, TextAnchor.MiddleLeft, 0, fill: true);
            text.color = Color.white;
            text.supportRichText = false;
            InsetHorizontally(text.rectTransform, 12f);

            input.textComponent = text;
            input.placeholder = placeholder;
            input.text = defaultText;
            return input;
        }

        static void InsetHorizontally(RectTransform rect, float inset)
        {
            rect.offsetMin = new Vector2(inset, rect.offsetMin.y);
            rect.offsetMax = new Vector2(-inset, rect.offsetMax.y);
        }

        static RectTransform AddRect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        /// <param name="fill">true = stretch to fill the parent rect (used for
        /// text nested inside a button/input field); false = a standalone row
        /// under a VerticalLayoutGroup, sized to <paramref name="height"/>.</param>
        static Text AddLabel(Transform parent, string name, int size, TextAnchor align, float height, bool fill = false)
        {
            var rect = fill
                ? AddRect(parent, name, Vector2.zero, Vector2.one)
                : AddRect(parent, name, Vector2.zero, Vector2.zero);

            if (!fill)
            {
                rect.sizeDelta = new Vector2(0, height);
                rect.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            }

            var text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = align;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }
    }
}
