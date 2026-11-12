using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SolParaTodos
{
    /// <summary>
    /// Área invisible a pantalla completa (detrás del HUD) que convierte el arrastre
    /// en rotación de cámara y el pellizco (dos dedos) en zoom.
    /// Al estar detrás de los botones, los toques sobre la UI no llegan aquí.
    /// El controlador consume los deltas acumulados cada frame.
    /// </summary>
    [AddComponentMenu("Sol Para Todos/SPT Área de Mirada Táctil")]
    public class SPTTouchLookArea : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private readonly Dictionary<int, Vector2> _punteros = new Dictionary<int, Vector2>();
        private Vector2 _deltaAcumulado;
        private float _pellizcoAcumulado;
        private float _distanciaPrevia;

        /// <summary>Número de dedos/punteros actualmente presionados en el área.</summary>
        public int PunterosActivos { get { return _punteros.Count; } }

        public void OnPointerDown(PointerEventData eventData)
        {
            _punteros[eventData.pointerId] = eventData.position;
            if (_punteros.Count == 2) _distanciaPrevia = DistanciaEntreDos();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_punteros.ContainsKey(eventData.pointerId)) return;
            _punteros[eventData.pointerId] = eventData.position;

            if (_punteros.Count >= 2)
            {
                float d = DistanciaEntreDos();
                _pellizcoAcumulado += d - _distanciaPrevia;
                _distanciaPrevia = d;
            }
            else
            {
                _deltaAcumulado += eventData.delta;
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _punteros.Remove(eventData.pointerId);
            if (_punteros.Count == 2) _distanciaPrevia = DistanciaEntreDos();
        }

        private void OnDisable()
        {
            _punteros.Clear();
            _deltaAcumulado = Vector2.zero;
            _pellizcoAcumulado = 0f;
        }

        /// <summary>Devuelve el arrastre acumulado (píxeles) y lo reinicia.</summary>
        public Vector2 ConsumirArrastre()
        {
            Vector2 d = _deltaAcumulado;
            _deltaAcumulado = Vector2.zero;
            return d;
        }

        /// <summary>Devuelve el cambio de distancia del pellizco (píxeles) y lo reinicia. Positivo = acercar.</summary>
        public float ConsumirPellizco()
        {
            float p = _pellizcoAcumulado;
            _pellizcoAcumulado = 0f;
            return p;
        }

        private float DistanciaEntreDos()
        {
            Vector2 a = Vector2.zero, b = Vector2.zero;
            int i = 0;
            foreach (KeyValuePair<int, Vector2> kv in _punteros)
            {
                if (i == 0) a = kv.Value; else if (i == 1) b = kv.Value; else break;
                i++;
            }
            return Vector2.Distance(a, b);
        }
    }
}
