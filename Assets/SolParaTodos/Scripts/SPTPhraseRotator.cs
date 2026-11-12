using TMPro;
using UnityEngine;

namespace SolParaTodos
{
    /// <summary>
    /// Rota frases de impacto en el menú de inicio con un fundido suave.
    /// Usa tiempo no escalado para funcionar aunque la simulación esté en pausa.
    /// </summary>
    [AddComponentMenu("Sol Para Todos/SPT Rotador de Frases")]
    public class SPTPhraseRotator : MonoBehaviour
    {
        public TMP_Text texto;
        public CanvasGroup grupo;
        [TextArea(2, 4)] public string[] frases;
        public float segundosVisible = 5f;
        public float segundosFundido = 0.6f;

        private int _indice;
        private float _t;

        private void OnEnable()
        {
            _t = 0f;
            Mostrar(_indice);
        }

        private void Update()
        {
            if (frases == null || frases.Length == 0 || texto == null) return;
            _t += Time.unscaledDeltaTime;
            float ciclo = segundosVisible + 2f * segundosFundido;

            if (grupo != null)
            {
                float a;
                if (_t < segundosFundido) a = _t / segundosFundido;
                else if (_t > segundosVisible + segundosFundido) a = 1f - (_t - segundosVisible - segundosFundido) / segundosFundido;
                else a = 1f;
                grupo.alpha = Mathf.Clamp01(a);
            }

            if (_t >= ciclo)
            {
                _t = 0f;
                _indice = (_indice + 1) % frases.Length;
                Mostrar(_indice);
            }
        }

        private void Mostrar(int i)
        {
            if (frases == null || frases.Length == 0 || texto == null) return;
            texto.text = frases[Mathf.Clamp(i, 0, frases.Length - 1)];
            if (grupo != null) grupo.alpha = 0f;
        }
    }
}
