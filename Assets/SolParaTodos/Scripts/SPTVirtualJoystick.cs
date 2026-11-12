using UnityEngine;
using UnityEngine.EventSystems;

namespace SolParaTodos
{
    /// <summary>
    /// Joystick virtual táctil (uGUI). Funciona con InputSystemUIInputModule porque
    /// solo usa las interfaces de EventSystem, sin leer dispositivos directamente.
    /// Valor: vector normalizado (-1..1) en <see cref="Valor"/>.
    /// </summary>
    [AddComponentMenu("Sol Para Todos/SPT Joystick Virtual")]
    public class SPTVirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [Tooltip("Fondo circular del joystick (área de arrastre).")]
        public RectTransform fondo;
        [Tooltip("Perilla que se mueve con el dedo.")]
        public RectTransform perilla;
        [Range(0f, 0.5f)] public float zonaMuerta = 0.08f;

        public Vector2 Valor { get; private set; }
        public bool Activo { get; private set; }

        private Canvas _canvas;

        private void Awake()
        {
            if (fondo == null) fondo = transform as RectTransform;
            _canvas = GetComponentInParent<Canvas>();
        }

        private void OnDisable()
        {
            Soltar();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            Activo = true;
            OnDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (fondo == null) return;
            Camera cam = null;
            if (_canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay) cam = _canvas.worldCamera;

            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(fondo, eventData.position, cam, out local)) return;

            Vector2 radio = fondo.rect.size * 0.5f;
            Vector2 v = new Vector2(radio.x > 0f ? local.x / radio.x : 0f, radio.y > 0f ? local.y / radio.y : 0f);
            if (v.sqrMagnitude > 1f) v.Normalize();

            if (perilla != null) perilla.anchoredPosition = new Vector2(v.x * radio.x, v.y * radio.y);

            Valor = v.magnitude < zonaMuerta ? Vector2.zero : v;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            Soltar();
        }

        private void Soltar()
        {
            Activo = false;
            Valor = Vector2.zero;
            if (perilla != null) perilla.anchoredPosition = Vector2.zero;
        }
    }
}
