using UnityEngine;

namespace SolParaTodos
{
    /// <summary>
    /// Marcador vacío que identifica los objetos creados por el constructor
    /// "Tools/Sol Para Todos/Construir Escena Completa y UI".
    /// Al reconstruir, solo se eliminan los objetos que llevan este componente,
    /// de modo que la jerarquía original (Filas, Pasto, Cámaras, Luz, Volume) nunca se toca.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Sol Para Todos/SPT Generado (marcador)")]
    public class SPTGenerado : MonoBehaviour
    {
    }
}
