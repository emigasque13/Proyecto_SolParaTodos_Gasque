using UnityEngine;
using UnityEngine.UI;

namespace SolParaTodos
{
    /// <summary>
    /// Ajusta el CanvasScaler a la orientación del dispositivo (el proyecto usa auto-rotación)
    /// y encaja el contenedor "SafeArea" dentro de Screen.safeArea (notch / barras del sistema).
    /// Vertical: referencia 1080x1920, ajuste por ancho. Horizontal: 1920x1080, ajuste por alto.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(CanvasScaler))]
    [AddComponentMenu("Sol Para Todos/SPT Canvas Responsivo")]
    public class SPTResponsiveCanvas : MonoBehaviour
    {
        public Vector2 referenciaVertical = new Vector2(1080f, 1920f);
        public Vector2 referenciaHorizontal = new Vector2(1920f, 1080f);
        [Tooltip("Contenedor cuyo RectTransform se ajusta al área segura de la pantalla.")]
        public RectTransform areaSegura;

        private CanvasScaler _scaler;
        private int _ancho = -1, _alto = -1;
        private Rect _seguraPrevia;

        private void OnEnable()
        {
            _scaler = GetComponent<CanvasScaler>();
            Aplicar(true);
        }

        private void Update()
        {
            Aplicar(false);
        }

        private void Aplicar(bool forzar)
        {
            if (_scaler == null) _scaler = GetComponent<CanvasScaler>();
            int w = Screen.width, h = Screen.height;
            Rect segura = Screen.safeArea;
            if (!forzar && w == _ancho && h == _alto && segura == _seguraPrevia) return;
            _ancho = w; _alto = h; _seguraPrevia = segura;

            bool horizontal = w > h;
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            _scaler.referenceResolution = horizontal ? referenciaHorizontal : referenciaVertical;
            _scaler.matchWidthOrHeight = horizontal ? 1f : 0f;

            if (areaSegura != null && w > 0 && h > 0)
            {
                Vector2 min = segura.position;
                Vector2 max = segura.position + segura.size;
                min.x /= w; min.y /= h; max.x /= w; max.y /= h;
                areaSegura.anchorMin = min;
                areaSegura.anchorMax = max;
                areaSegura.offsetMin = Vector2.zero;
                areaSegura.offsetMax = Vector2.zero;
            }
        }
    }
}
