using UnityEngine;
using UnityEngine.EventSystems;

namespace SolParaTodos
{
    /// <summary>
    /// Micro-interacción de los botones del HUD: se encogen levemente al pulsarse y vuelven con suavidad.
    /// Usa tiempo no escalado (funciona aunque la simulación esté en pausa) y no genera basura.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Sol Para Todos/SPT Feedback de Botón")]
    public class SPTButtonFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [Range(0.85f, 1f)] public float escalaPresionado = 0.95f;
        public float velocidad = 18f;

        private bool _presionado;
        private float _escala = 1f;
        private Vector3 _base = Vector3.one;

        private void Awake() { _base = transform.localScale; }

        private void OnEnable()
        {
            _presionado = false;
            _escala = 1f;
            transform.localScale = _base;
        }

        public void OnPointerDown(PointerEventData e) { _presionado = true; }
        public void OnPointerUp(PointerEventData e) { _presionado = false; }
        public void OnPointerExit(PointerEventData e) { _presionado = false; }

        private void Update()
        {
            float objetivo = _presionado ? escalaPresionado : 1f;
            if (Mathf.Approximately(_escala, objetivo)) return;
            _escala = Mathf.MoveTowards(_escala, objetivo, velocidad * Time.unscaledDeltaTime * 0.5f);
            transform.localScale = _base * _escala;
        }
    }
}
