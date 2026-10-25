using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    public class Object
    {
        private static readonly List<Object> PendingDestruction = new List<Object>();
        public string name { get; set; } = string.Empty;

        /// <summary>
        /// What <c>Object.Destroy</c> leaves behind in the game: the C# reference survives, and
        /// Unity's equality answers it as null. A test sets it to model an unloaded scene.
        /// </summary>
        public bool destroyed { get; set; }

        public static bool operator ==(Object? x, Object? y)
        {
            var xNull = x is null || x.destroyed;
            var yNull = y is null || y.destroyed;
            return xNull || yNull ? xNull && yNull : ReferenceEquals(x, y);
        }

        public static bool operator !=(Object? x, Object? y) => !(x == y);

        public override bool Equals(object? other) => ReferenceEquals(this, other);

        public override int GetHashCode() => base.GetHashCode();

        public static T Instantiate<T>(T original, Transform parent, bool worldPositionStays) where T : Object, new()
        {
            var clone = original is GameObject originalGameObject &&
                        originalGameObject.transform is RectTransform
                ? (T)(Object)new GameObject(original.name, typeof(RectTransform))
                : new T();
            if (clone is GameObject gameObject)
            {
                gameObject.transform.SetParent(parent, worldPositionStays);
            }

            return clone;
        }

        public static void Destroy(Object obj)
        {
            if (obj is null || obj.destroyed || PendingDestruction.Contains(obj)) return;
            PendingDestruction.Add(obj);
        }

        /// <summary>Advances the portable model past Unity's deferred destruction boundary.</summary>
        public static void FlushDestroyQueue()
        {
            var pending = PendingDestruction.ToArray();
            PendingDestruction.Clear();
            foreach (var obj in pending) DestroyNow(obj);
        }

        internal static void DestroyNow(Object obj)
        {
            if (obj.destroyed) return;
            if (obj is GameObject gameObject) gameObject.DestroyOwnedObjects();
            else if (obj is Component component)
            {
                if (component is Transform) return; // Unity does not remove a Transform alone.
                component.gameObject.RemoveComponent(component);
            }
            obj.destroyed = true;
        }
    }

    public class Component : Object
    {
        public GameObject gameObject { get; internal set; } = null!;

        public Transform transform { get; internal set; } = null!;

        public T? GetComponent<T>() where T : Component => gameObject?.GetComponent<T>();

        public Component? GetComponent(Type type) => gameObject?.GetComponent(type);

        public T? GetComponentInChildren<T>(bool includeInactive = false) where T : Component => gameObject?.GetComponentInChildren<T>(includeInactive);

        public T[] GetComponents<T>() where T : Component => gameObject?.GetComponents<T>() ?? Array.Empty<T>();
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; } = true;
    }

    public class MonoBehaviour : Behaviour
    {
        public Coroutine StartCoroutine(IEnumerator routine) => new Coroutine(routine);
    }

    public sealed class Coroutine
    {
        public Coroutine(IEnumerator routine) => Routine = routine;
        public IEnumerator Routine { get; }
    }

    public sealed class WaitForEndOfFrame { }

    public class Texture2D : Object
    {
        public Texture2D() : this(1920, 1080) { }
        public Texture2D(int width, int height)
        {
            this.width = width;
            this.height = height;
        }
        public int width { get; }
        public int height { get; }
        public Color GetPixelBilinear(float u, float v) => default;
        public void SetPixel(int x, int y, Color color) { }
        public void Apply(bool updateMipmaps = true, bool makeNoLongerReadable = false) { }
        public byte[] EncodeToPNG() => new byte[] { 137, 80, 78, 71 };
    }

    public class GameObject : Object
    {
        private readonly List<Component> _components = new List<Component>();

        public GameObject()
            : this(string.Empty)
        {
        }

        public GameObject(string name, params Type[] components)
        {
            this.name = name;
            transform = components.Contains(typeof(RectTransform))
                ? new RectTransform { gameObject = this, name = name }
                : new Transform { gameObject = this, name = name };
            transform.transform = transform;
            _components.Add(transform);
            foreach (var type in components.Where(type => type != typeof(RectTransform) && typeof(Component).IsAssignableFrom(type)))
            {
                var component = (Component)Activator.CreateInstance(type)!;
                component.gameObject = this;
                component.transform = transform;
                component.name = name;
                _components.Add(component);
            }
        }

        public Transform transform { get; }

        public bool activeInHierarchy { get; set; } = true;
        public bool activeSelf { get; private set; } = true;
        public UnityEngine.SceneManagement.Scene scene { get; set; } =
            new UnityEngine.SceneManagement.Scene("Main");

        public static GameObject? Find(string name) => null;

        public void SetActive(bool value)
        {
            activeSelf = value;
            activeInHierarchy = value;
        }

        public T? GetComponent<T>() where T : Component => _components.OfType<T>().FirstOrDefault();

        public T? GetComponentInChildren<T>(bool includeInactive = false) where T : Component
        {
            if (GetComponent<T>() is { } own) return own;
            for (var index = 0; index < transform.childCount; index++)
            {
                var child = transform.GetChild(index).gameObject;
                if ((includeInactive || child.activeInHierarchy) &&
                    child.GetComponentInChildren<T>(includeInactive) is { } found)
                    return found;
            }
            return null;
        }

        public Component? GetComponent(Type type) => _components.FirstOrDefault(type.IsInstanceOfType);

        public T[] GetComponents<T>() where T : Component => _components.OfType<T>().ToArray();

        internal void DestroyOwnedObjects()
        {
            while (transform.childCount > 0)
            {
                var child = transform.GetChild(transform.childCount - 1);
                child.SetParent(null, false);
                Object.DestroyNow(child.gameObject);
            }
            transform.SetParent(null, false);
            SetActive(false);
            foreach (var component in _components) component.destroyed = true;
            _components.Clear();
        }

        internal void RemoveComponent(Component component)
        {
            _components.Remove(component);
        }

        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this, transform = transform, name = name };
            _components.Add(component);
            return component;
        }
    }

    public class Transform : Component
    {
        private readonly List<Transform> _children = new List<Transform>();

        public Transform? parent { get; private set; }

        public int childCount => _children.Count;

        public void SetParent(Transform? parent, bool worldPositionStays)
        {
            if (ReferenceEquals(this.parent, parent)) return;
            this.parent?._children.Remove(this);
            this.parent = parent;
            parent?._children.Add(this);
        }

        public int GetSiblingIndex() => parent?._children.IndexOf(this) ?? 0;

        public void SetSiblingIndex(int index)
        {
            if (parent is null) return;
            parent._children.Remove(this);
            parent._children.Insert(Math.Max(0, Math.Min(index, parent._children.Count)), this);
        }

        public void SetAsLastSibling()
        {
            if (parent is null) return;
            parent._children.Remove(this);
            parent._children.Add(this);
        }

        public Transform GetChild(int index) => _children[index];

        public bool IsChildOf(Transform candidate)
        {
            for (var node = this; node is not null; node = node.parent)
                if (ReferenceEquals(node, candidate)) return true;
            return false;
        }
    }

    public class RectTransform : Transform
    {
        public Vector2 anchorMin { get; set; }
        public Vector2 anchorMax { get; set; }
        public Vector2 offsetMin { get; set; }
        public Vector2 offsetMax { get; set; }
        public Vector2 pivot { get; set; }
        public Vector2 anchoredPosition { get; set; }
        public Vector2 sizeDelta { get; set; }
        public Rect rect { get; set; } = new Rect(0f, 0f, 44f, 44f);
    }

    public readonly struct Vector2
    {
        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public readonly float x;
        public readonly float y;
        public static Vector2 zero => new Vector2(0f, 0f);
        public static Vector2 one => new Vector2(1f, 1f);
    }

    public readonly struct Vector3
    {
        public Vector3(float x, float y, float z = 0f)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public readonly float x;
        public readonly float y;
        public readonly float z;
    }

    public readonly struct Color
    {
        public Color(float r, float g, float b, float a = 1f)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public readonly float r;
        public readonly float g;
        public readonly float b;
        public readonly float a;
        public static Color white => new Color(1f, 1f, 1f, 1f);

        public static Color Lerp(Color a, Color b, float t) => t < 0.5f ? a : b;
    }

    public readonly struct Color32
    {
        public Color32(byte r, byte g, byte b, byte a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public readonly byte r;
        public readonly byte g;
        public readonly byte b;
        public readonly byte a;

        public static implicit operator Color32(Color color) => new Color32(
            (byte)(color.r * byte.MaxValue),
            (byte)(color.g * byte.MaxValue),
            (byte)(color.b * byte.MaxValue),
            (byte)(color.a * byte.MaxValue));
    }

    public class CanvasRenderer : Component
    {
    }

    public class CanvasGroup : Behaviour
    {
    }

    public class Material : Object
    {
    }

    public class Sprite : Object
    {
    }

    public class ScriptableObject : Object
    {
    }

    public enum KeyCode
    {
        None = 0,
        Equals = 61,
        Minus = 45,
        Alpha0 = 48,
        Alpha1 = 49,
        Alpha2 = 50,
        Alpha3 = 51,
        Alpha4 = 52,
        Alpha5 = 53,
        Alpha6 = 54,
        Alpha7 = 55,
        Alpha8 = 56,
        Alpha9 = 57,
        Space = 32,
        Q = 113,
        W = 119,
        E = 101,
        R = 114,
        T = 116,
        X = 120,
        Y = 121,
        Z = 122,
        J = 106,
        M = 109,
        Keypad1 = 257,
        Keypad2 = 258,
        Keypad3 = 259,
        Keypad4 = 260,
        Keypad5 = 261,
        Keypad6 = 262,
        Keypad7 = 263,
        Keypad8 = 264,
        Keypad9 = 265,
        UpArrow = 273,
        DownArrow = 274,
        RightArrow = 275,
        LeftArrow = 276,
        F7 = 288,
        F8 = 289,
        RightShift = 303,
        LeftShift = 304,
        RightControl = 305,
        LeftControl = 306,
        RightAlt = 307,
        LeftAlt = 308,
    }

    public static class Time
    {
        public static float timeScale { get; set; } = 1.0f;
        public static float fixedDeltaTime { get; set; } = 0.02f;
        public static float deltaTime { get; set; } = 0.016f;
        public static float unscaledDeltaTime { get; set; } = 0.016f;
        public static float realtimeSinceStartup { get; set; }
        public static int frameCount { get; set; }
    }

    public static class Application
    {
        public static string persistentDataPath { get; set; } = string.Empty;
    }

    public static class ScreenCapture
    {
        public static void CaptureScreenshot(string filename)
        {
        }

        public static Texture2D CaptureScreenshotAsTexture() => new Texture2D();
    }

    public static class Resources
    {
        public static List<Object> Objects { get; } = new List<Object>();
        public static Object[] FindObjectsOfTypeAll(Type type) =>
            Objects.Where(type.IsInstanceOfType).ToArray();
    }

    public readonly struct Rect
    {
        public Rect(float x, float y, float width, float height)
        {
            this.x = x;
            this.y = y;
            this.width = width;
            this.height = height;
        }

        public readonly float x;
        public readonly float y;
        public readonly float width;
        public readonly float height;
        public float xMin => x;
        public float xMax => x + width;
        public float yMin => y;
        public float yMax => y + height;
    }

    public static class GUI
    {
        public static void Box(Rect position, string text)
        {
        }
    }
}

namespace UnityEngine.Profiling
{
    public static class Profiler
    {
        public static long MonoUsedSize { get; set; }

        public static long GetMonoUsedSizeLong() => MonoUsedSize;

        public static long MonoHeapSize { get; set; }

        public static long GetMonoHeapSizeLong() => MonoHeapSize;
    }
}

namespace Unity.Profiling
{
    public readonly struct ProfilerCategory
    {
        public static ProfilerCategory Memory => default;
    }

    public enum ProfilerRecorderOptions
    {
        Default = 0,
    }

    public struct ProfilerRecorder : System.IDisposable
    {
        public static ProfilerRecorder StartNew(
            ProfilerCategory category,
            string statName,
            int capacity = 1,
            ProfilerRecorderOptions options = ProfilerRecorderOptions.Default) => default;

        public bool Valid => false;

        public long LastValue => 0;

        public void Dispose()
        {
        }
    }
}

namespace UnityEngine.Events
{
    public delegate void UnityAction();

    public class UnityEvent
    {
        private readonly List<UnityAction> _listeners = new List<UnityAction>();

        public void AddListener(UnityAction listener) => _listeners.Add(listener);

        public void RemoveListener(UnityAction listener) => _listeners.Remove(listener);

        public void Invoke()
        {
            foreach (var listener in _listeners.ToArray()) listener();
        }
    }
}

namespace UnityEngine.UI
{
    public class Graphic : UnityEngine.Behaviour
    {
        public UnityEngine.Color color { get; set; } = UnityEngine.Color.white;
        public bool raycastTarget { get; set; } = true;
        public UnityEngine.RectTransform rectTransform =>
            (UnityEngine.RectTransform)transform;

        protected void SetVerticesDirty()
        {
        }
    }

    public class MaskableGraphic : Graphic
    {
        protected virtual void OnPopulateMesh(VertexHelper helper)
        {
        }
    }

    public sealed class VertexHelper
    {
        public int currentVertCount { get; private set; }

        public void Clear() => currentVertCount = 0;

        public void AddVert(
            UnityEngine.Vector3 position,
            UnityEngine.Color32 color,
            UnityEngine.Vector2 uv0) =>
            currentVertCount++;

        public void AddTriangle(int index0, int index1, int index2)
        {
        }
    }

    public class Image : Graphic
    {
        public enum Type
        {
            Simple,
            Sliced,
            Tiled,
            Filled,
        }

        public UnityEngine.Sprite? sprite { get; set; }
        public bool preserveAspect { get; set; }
        public Type type { get; set; }
    }

    public class Selectable : UnityEngine.Behaviour
    {
        public bool interactable { get; set; } = true;

        public bool IsInteractable() => interactable;

        public Graphic? targetGraphic { get; set; }
    }

    public class Button : Selectable
    {
        public ButtonClickedEvent onClick { get; } = new ButtonClickedEvent();

        public sealed class ButtonClickedEvent
        {
            private readonly List<UnityEngine.Events.UnityAction> _listeners = new List<UnityEngine.Events.UnityAction>();

            public void AddListener(UnityEngine.Events.UnityAction listener) => _listeners.Add(listener);

            public void RemoveListener(UnityEngine.Events.UnityAction listener) => _listeners.Remove(listener);

            public void RemoveAllListeners() => _listeners.Clear();

            public void Invoke()
            {
                foreach (var listener in _listeners.ToArray()) listener();
            }
        }
    }

    public class RectMask2D : UnityEngine.Behaviour
    {
    }

    public class LayoutElement : UnityEngine.Behaviour
    {
        public bool ignoreLayout { get; set; }
    }

    public class ScrollRect : UnityEngine.Behaviour
    {
        public enum MovementType
        {
            Unrestricted,
            Elastic,
            Clamped,
        }

        public bool horizontal { get; set; }
        public bool vertical { get; set; }
        public bool inertia { get; set; }
        public MovementType movementType { get; set; }
        public float scrollSensitivity { get; set; }
        public float horizontalNormalizedPosition { get; set; }
        public float verticalNormalizedPosition { get; set; }
        public UnityEngine.RectTransform? viewport { get; set; }
        public UnityEngine.RectTransform? content { get; set; }
    }
}

namespace TMPro
{
    public enum TextAlignmentOptions
    {
        Center,
        Midline,
        MidlineLeft,
        TopLeft,
    }

    public enum TextOverflowModes
    {
        Overflow,
        Ellipsis,
    }

    public enum TextWrappingModes
    {
        NoWrap,
        Normal,
    }

    public class TMP_FontAsset : UnityEngine.Object
    {
    }

    public class TextMeshProUGUI : UnityEngine.UI.Graphic
    {
        public string text { get; set; } = string.Empty;
        public TMP_FontAsset? font { get; set; }
        public UnityEngine.Material? fontSharedMaterial { get; set; }
        public float fontSize { get; set; } = 16f;
        public TextAlignmentOptions alignment { get; set; }
        public TextWrappingModes textWrappingMode { get; set; }
        public TextOverflowModes overflowMode { get; set; }

        public UnityEngine.Vector2 GetPreferredValues(string value, float width, float height)
        {
            var characterWidth = Math.Max(1f, fontSize * 0.5f);
            var charactersPerLine = Math.Max(1, (int)(Math.Max(1f, width) / characterWidth));
            var lineCount = 0;
            var maximumLineLength = 0;
            foreach (var line in (value ?? string.Empty).Split('\n'))
            {
                maximumLineLength = Math.Max(maximumLineLength, line.Length);
                lineCount += Math.Max(1, (line.Length + charactersPerLine - 1) / charactersPerLine);
            }

            return new UnityEngine.Vector2(
                Math.Min(Math.Max(1f, width), maximumLineLength * characterWidth),
                Math.Max(fontSize, lineCount * fontSize * 1.2f));
        }
    }

    public class TMP_InputField : UnityEngine.UI.Selectable
    {
        public enum LineType
        {
            SingleLine,
            MultiLineSubmit,
            MultiLineNewline,
        }

        public UnityEngine.RectTransform? textViewport { get; set; }
        public TextMeshProUGUI? textComponent { get; set; }
        public string text { get; set; } = string.Empty;
          public LineType lineType { get; set; }
          public OnChangeEvent onValueChanged { get; } = new OnChangeEvent();
          public OnChangeEvent onSelect { get; } = new OnChangeEvent();
          public OnChangeEvent onEndEdit { get; } = new OnChangeEvent();

        public sealed class OnChangeEvent
        {
            private readonly List<Action<string>> _listeners = new List<Action<string>>();

            public void AddListener(Action<string> listener) => _listeners.Add(listener);

            public void Invoke(string value)
            {
                foreach (var listener in _listeners.ToArray()) listener(value);
            }
        }
    }
}

namespace UnityEngine.SceneManagement
{
    public enum LoadSceneMode
    {
        Single = 0,
        Additive = 1,
    }

    public readonly struct Scene
    {
        public Scene(string name)
        {
            this.name = name;
        }

        public readonly string name;

        public bool IsValid() => name is not null;

        public bool isLoaded => IsValid();
    }

    public static class SceneManager
    {
        public static event Action<Scene, Scene>? activeSceneChanged;

        public static event Action<Scene, LoadSceneMode>? sceneLoaded;

        public static Scene ActiveScene { get; set; } = new Scene("Main");

        public static Scene GetActiveScene() => ActiveScene;

        public static void RaiseActiveSceneChanged(Scene previous, Scene next)
        {
            ActiveScene = next;
            activeSceneChanged?.Invoke(previous, next);
        }

        public static void RaiseSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            sceneLoaded?.Invoke(scene, mode);
        }
    }
}
