// ============================================================================
//  Sol Para Todos — SolarFieldController
//  Emiliano Gasque · Ingeniería en Tecnologías Digitales · Universidad Marista de Mérida
//  Gráficas por Computadora — Prof. Luis de Jesús López Hernández
//
//  Controlador principal de la escena SampleScene:
//   · Ciclo solar 6:00–18:00 (rotación de la Directional Light en X, sombras dinámicas)
//   · Telemetría: eficiencia por producto punto (Vector3.Dot) sol·normal, irradiancia, kW
//   · Feedback visual de las celdas con MaterialPropertyBlock (sin instanciar materiales)
//   · Conmutador Día / Noche (Volume nocturno + indicadores de inversores)
//   · Selector de 3 vistas (panorámica, detalle LOD0, recorrido estructural táctil)
//   · Estados de UI: Menú de inicio, Acerca de, HUD, Fichas técnicas
//
//  v2 (actualización): modo "Crepúsculo / Noche de Laboratorio" (luna + ambiente azul + indicadores
//  emisivos con Bloom), cielo y niebla dinámicos que ocultan el horizonte, HUD con detalles
//  desplegables, reloj sin deformación y banner de ayuda que se oculta solo.
//
//  Todas las referencias las conecta automáticamente:
//   Tools > Sol Para Todos > Construir Escena Completa y UI
// ============================================================================
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SolParaTodos
{
    public enum ModoFeedback
    {
        [Tooltip("Pedido por el proyecto: un MaterialPropertyBlock por renderer de celda. Los renderers con bloque salen del SRP Batcher mientras el brillo está activo.")]
        MaterialPropertyBlock = 0,
        [Tooltip("Una sola copia del material creada al iniciar y compartida por todas las celdas: conserva el SRP Batcher todo el tiempo.")]
        MaterialCompartidoRuntime = 1,
        Desactivado = 2
    }

    public enum ModoCamaras
    {
        [Tooltip("Una sola cámara (la principal) viaja suavemente entre los 3 puntos de vista SPT_Vista_*.")]
        InterpolarCamaraPrincipal = 0,
        [Tooltip("Activa en exclusiva Main Camera / Main Camera 2 / Main Camera 3 (corte directo).")]
        CambiarEntreCamaras = 1
    }

    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    [AddComponentMenu("Sol Para Todos/Solar Field Controller")]
    public class SolarFieldController : MonoBehaviour
    {
        // ------------------------------------------------------------------ Escena
        [Header("Escena (conectado por el constructor)")]
        public Light sol;
        public Camera camaraPrincipal;
        [Tooltip("Main Camera, Main Camera 2, Main Camera 3 (si existen).")]
        public Camera[] camarasEscena = new Camera[3];
        public Volume volumenGlobal;
        public Volume volumenNocturno;
        public Transform[] filas;
        [Tooltip("Anclas de las 3 vistas: 0 Panorámica, 1 Detalle de panel, 2 Inspección estructural.")]
        public Transform[] vistas = new Transform[3];
        [Tooltip("Distancia al punto de interés de cada vista (orbitado con arrastre).")]
        public float[] distanciaOrbita = { 80f, 6f, 0f };
        [Tooltip("Límites del recorrido a pie (Vista 3).")]
        public Bounds limitesRecorrido;
        public Renderer[] ledsIndicadores;

        // ------------------------------------------------------------------ Cielo, niebla y ambiente (v2)
        [Header("Cielo, niebla y ambiente (v2)")]
        [Tooltip("Material con el shader SolParaTodos/SkyGradient. Se copia en runtime (el asset no se modifica).")]
        public Material materialCielo;
        [Tooltip("Controla Skybox, niebla lineal y ambiente Trilight según la hora y el modo noche.")]
        public bool controlarAtmosfera = true;
        public float nieblaInicio = 220f;
        public float nieblaFin = 1100f;
        [Range(0f, 2f)] public float intensidadAmbienteDia = 1f;
        public Color cieloZenitDia = new Color(0.13f, 0.34f, 0.74f, 1f);
        public Color cieloHorizonteDia = new Color(0.66f, 0.80f, 0.94f, 1f);
        public Color cieloZenitAmanecer = new Color(0.28f, 0.34f, 0.58f, 1f);
        public Color cieloHorizonteAmanecer = new Color(1.00f, 0.66f, 0.42f, 1f);
        public Color ambienteCieloDia = new Color(0.46f, 0.58f, 0.80f, 1f);
        public Color ambienteEcuadorDia = new Color(0.52f, 0.60f, 0.68f, 1f);
        public Color ambienteSueloDia = new Color(0.30f, 0.28f, 0.24f, 1f);

        // ------------------------------------------------------------------ Ciclo solar
        [Header("Ciclo solar")]
        [Range(6f, 18f)] public float hora = 10f;
        public float horaAmanecer = 6f;
        public float horaAtardecer = 18f;
        [Tooltip("Ángulo Y (azimut) de la luz. El constructor copia el valor original de tu Directional Light.")]
        public float azimutSol = 12.678f;
        [Tooltip("Gira el azimut para que el sol salga y se ponga perpendicular a la cara de los paneles (curva simétrica con pico al mediodía).")]
        public bool alinearTrayectoriaConPaneles = true;
        [Tooltip("Inclina la trayectoria solar hacia el lado al que miran los paneles (latitud de Mérida aprox. 21°). 0 = el sol pasa por el cenit.")]
        [Range(0f, 45f)] public float latitudGrados = 21f;
        [Tooltip("Intensidad de la luz al mediodía. El constructor copia la intensidad original.")]
        public float intensidadSolMax = 2f;
        [Tooltip("Qué tan rápido sigue la luz al slider (mayor = más rápido).")]
        public float suavizadoSol = 5f;
        [Tooltip("Horas simuladas por segundo en modo automático.")]
        public float horasPorSegundoAuto = 0.6f;

        // ------------------------------------------------------------------ Modelo energético
        [Header("Modelo energético (estimación didáctica)")]
        [Tooltip("Módulos fotovoltaicos por unidad Panel_LOD (el constructor los cuenta en LOD0).")]
        public int modulosPorUnidad = 6;
        [Tooltip("Potencia nominal de cada módulo en kWp (550 W típico de un módulo moderno).")]
        public float potenciaModuloKWp = 0.55f;
        [Tooltip("Performance Ratio: pérdidas por temperatura, cableado, inversor, suciedad.")]
        [Range(0.5f, 1f)] public float performanceRatio = 0.8f;
        [Tooltip("Demanda media de un hogar en kW (≈250 kWh/mes).")]
        public float consumoHogarKW = 0.35f;
        [Tooltip("Factor de emisión de la red eléctrica (kg CO2 por kWh).")]
        public float factorEmisionKgPorKWh = 0.438f;
        [Tooltip("Normal (hacia el cielo) de cada unidad de panel, en espacio mundo. El constructor la calcula.")]
        public Vector3[] normalesPaneles;

        // ------------------------------------------------------------------ Feedback de celdas
        [Header("Feedback visual de celdas (P. Fotovoltaico)")]
        public ModoFeedback modoFeedback = ModoFeedback.MaterialPropertyBlock;
        public Material materialCeldas;
        public Renderer[] renderersCeldas;
        [ColorUsage(false, true)] public Color colorEnergia = new Color(0.20f, 0.55f, 1.0f, 1f);
        [Tooltip("Intensidad máxima de emisión al mediodía.")]
        public float emisionMax = 0.35f;
        [Tooltip("Tinte máximo del color base hacia el color energético.")]
        [Range(0f, 0.5f)] public float tinteMax = 0.12f;
        [Tooltip("Fracción de irradiancia (0–1) a partir de la cual empieza a brillar.")]
        [Range(0f, 1f)] public float umbralInicioBrillo = 0.55f;
        [Tooltip("Renderers actualizados por frame (reparte el costo de CPU entre frames).")]
        public int renderersPorFrame = 1800;

        // ------------------------------------------------------------------ Modo noche
        [Header("Modo Crepúsculo / Noche de Laboratorio")]
        [Tooltip("Intensidad de la luz lunar (relleno azul). Súbela si quieres ver más la silueta del parque.")]
        public float intensidadLuna = 0.45f;
        public Color colorLuna = new Color(0.50f, 0.62f, 1f, 1f);
        public float temperaturaLuna = 11000f;
        [Tooltip("Multiplicador del ambiente azul nocturno (Trilight). 1 = valor por defecto.")]
        [Range(0f, 2.5f)] public float ambienteNoche = 1.15f;
        public Color ambienteCieloNoche = new Color(0.17f, 0.25f, 0.52f, 1f);
        public Color ambienteEcuadorNoche = new Color(0.12f, 0.18f, 0.38f, 1f);
        public Color ambienteSueloNoche = new Color(0.05f, 0.08f, 0.17f, 1f);
        public Color cieloZenitNoche = new Color(0.015f, 0.03f, 0.11f, 1f);
        public Color cieloHorizonteNoche = new Color(0.07f, 0.13f, 0.29f, 1f);
        public float segundosTransicionNoche = 1.2f;
        [ColorUsage(false, true)] public Color colorLedGenerando = new Color(0.1f, 1f, 0.35f, 1f);
        [ColorUsage(false, true)] public Color colorLedEspera = new Color(1f, 0.62f, 0.1f, 1f);

        [Header("Indicadores luminosos de la granja (emisivos + Bloom)")]
        public Renderer[] acentosAzul;
        public Renderer[] acentosVerde;
        public Renderer[] acentosRojo;
        [ColorUsage(false, true)] public Color colorAcentoAzul = new Color(0.10f, 0.55f, 1.00f, 1f);
        [ColorUsage(false, true)] public Color colorAcentoVerde = new Color(0.10f, 1.00f, 0.40f, 1f);
        [ColorUsage(false, true)] public Color colorAcentoRojo = new Color(1.00f, 0.12f, 0.10f, 1f);
        [Tooltip("Intensidad HDR de los indicadores de día / de noche (con Bloom brillan fuerte).")]
        public float emisionAcentoDia = 0.6f;
        public float emisionAcentoNoche = 5f;

        // ------------------------------------------------------------------ Cámaras
        [Header("Cámaras")]
        public ModoCamaras modoCamaras = ModoCamaras.InterpolarCamaraPrincipal;
        public float duracionTransicion = 1.4f;
        public float sensibilidadMirada = 0.12f;
        public float velocidadRecorrido = 4f;
        public float velocidadOrbitaMenu = 3f;

        // ------------------------------------------------------------------ UI
        [Header("UI — Paneles")]
        public GameObject panelMenu;
        public GameObject panelAcerca;
        public GameObject panelHUD;
        public GameObject panelFichas;
        public GameObject bannerGuia;
        [Tooltip("Segundos que permanece visible el mensaje de ayuda antes de ocultarse solo (0 = no se oculta).")]
        public float segundosBanner = 9f;
        public GameObject contenedorJoystick;
        public SPTVirtualJoystick joystick;
        public SPTTouchLookArea areaMirada;

        [Header("UI — Controles y textos")]
        public Slider sliderHora;
        public TMP_Text textoHora;
        public TMP_Text textoFaseDia;
        public TMP_Text textoPotencia;
        public TMP_Text textoEficiencia;
        public Image barraEficiencia;
        public TMP_Text textoIrradiancia;
        public TMP_Text textoIncidencia;
        public TMP_Text textoElevacion;
        public TMP_Text textoHogares;
        public TMP_Text textoCO2;
        public TMP_Text textoCapacidad;
        public TMP_Text textoEstado;
        public TMP_Text textoFPS;
        public TMP_Text textoBotonDiaNoche;
        public TMP_Text textoBotonAuto;
        public TMP_Text textoVistaActual;
        public Button[] botonesVista = new Button[3];
        public Button[] botonesFicha = new Button[3];
        public TMP_Text textoFichaTitulo;
        public TMP_Text textoFichaCuerpo;
        public ScrollRect scrollFicha;
        [Header("UI — Estilo HUD (v2)")]
        public Color colorBotonNormal = new Color(0.04f, 0.055f, 0.075f, 0.72f);
        public Color colorBotonActivo = new Color(0.00f, 0.82f, 1.00f, 0.95f);
        public Color colorBarra = new Color(1.00f, 0.48f, 0.05f, 1f);
        public Color colorTextoActivo = new Color(0.02f, 0.05f, 0.08f, 1f);
        [Tooltip("Panel desplegable «Más detalles» (Irradiancia, Hogares, CO2, Elevación…).")]
        public RectTransform panelDetalles;
        public CanvasGroup grupoDetalles;
        public RectTransform iconoDetalles;
        public TMP_Text textoBotonDetalles;
        public Image imagenIconoNoche;
        public Sprite spriteLuna;
        public Sprite spriteSol;

        [Header("Contenido de fichas técnicas")]
        public string[] fichaTitulos;
        [TextArea(6, 20)] public string[] fichaCuerpos;

        // ------------------------------------------------------------------ Propiedades públicas (lectura)
        public float EficienciaCaptacion { get; private set; }
        public float PotenciaKW { get; private set; }
        public float IrradianciaPlano { get; private set; }
        public float ElevacionSolGrados { get; private set; }
        public float AnguloIncidenciaGrados { get; private set; }
        public float CapacidadInstaladaKWp { get { return (normalesPaneles != null ? normalesPaneles.Length : 0) * modulosPorUnidad * potenciaModuloKWp; } }
        public bool ModoNoche { get { return _modoNoche; } }
        public int VistaActual { get { return _vista; } }
        public bool DetallesAbiertos { get { return _detallesAbierto; } }

        public static readonly string[] NombresVista = { "Panorámica del parque solar", "Detalle de panel y celdas (LOD0)", "Inspección estructural y cimentación" };

        // ------------------------------------------------------------------ Estado interno
        private static readonly int IdEmission = Shader.PropertyToID("_EmissionColor");
        private static readonly int IdBaseColor = Shader.PropertyToID("_BaseColor");

        private enum Estado { Menu, Simulacion }
        private Estado _estado = Estado.Menu;

        private float _anguloSolActual;
        private bool _autoHora;
        private bool _modoNoche;
        private float _noche01;

        private float _intensidadOriginal;
        private float _temperaturaOriginal;
        private Color _colorLuzOriginal;
        private float _ambienteOriginal;
        private Quaternion _rotacionLuzOriginal;

        private MaterialPropertyBlock _mpb;
        private Material _materialRuntimeCeldas;
        private Color _colorBaseCeldas = Color.white;
        private float _brilloAplicado = -1f;
        private float _brilloObjetivo;
        private int _cursorMpb;
        private int _pendientesMpb;

        private Material _materialLed;

        // v2 — atmósfera
        private Material _cieloRuntime, _cieloOriginal;
        private Material _matAzul, _matVerde, _matRojo;
        private bool _ambienteGuardado;
        private AmbientMode _ambModoOrig;
        private Color _ambSkyOrig, _ambEqOrig, _ambGroundOrig, _fogColorOrig;
        private bool _fogOrig;
        private FogMode _fogModoOrig;
        private float _fogIniOrig, _fogFinOrig;
        private float _elev01;
        private bool _atmValida;
        private float _atmNoche, _atmElev, _atmAngulo;

        // v2 — HUD
        private bool _detallesAbierto;
        private float _detallesT;
        private float _tBanner;
        private float _barraSuave;

        private int _vista;
        private Pose[] _anclas = new Pose[3];
        private readonly float[] _yawOff = new float[3];
        private readonly float[] _pitchOff = new float[3];
        private readonly float[] _zoom = { 1f, 1f, 1f };
        private Vector3 _recorridoPos;
        private float _recorridoYaw, _recorridoPitch;
        private float _transT = 1f;
        private Vector3 _transDesdePos;
        private Quaternion _transDesdeRot;
        private Camera _camActiva;

        private float _tUI;
        private float _dtSuave = 1f / 60f;
        private int _ultimoMinutoMostrado = -1;
        private Vector3 _normalMedia = Vector3.up;
        private float _signoLatitud = 1f;

        // =================================================================== Ciclo de vida
        private void Awake()
        {
            if (sol == null) sol = BuscarLuzDireccional();
            if (camaraPrincipal == null) camaraPrincipal = Camera.main;

            if (sol != null)
            {
                _intensidadOriginal = sol.intensity;
                _temperaturaOriginal = sol.colorTemperature;
                _colorLuzOriginal = sol.color;
                _rotacionLuzOriginal = sol.transform.rotation;
                if (intensidadSolMax <= 0f) intensidadSolMax = Mathf.Max(0.5f, sol.intensity);
            }
            _ambienteOriginal = RenderSettings.ambientIntensity;

            PrepararNormales();
            PrepararTrayectoria();
            PrepararCeldas();
            PrepararLeds();
            PrepararAcentos();
            PrepararAtmosfera();
            PrepararAnclas();

            if (volumenNocturno != null) volumenNocturno.weight = 0f;
            if (fichaTitulos == null || fichaTitulos.Length == 0) fichaTitulos = FichasTitulosPorDefecto;
            if (fichaCuerpos == null || fichaCuerpos.Length == 0) fichaCuerpos = FichasCuerposPorDefecto;
        }

        private void Start()
        {
            hora = Mathf.Clamp(hora, horaAmanecer, horaAtardecer);
            _anguloSolActual = AnguloSolParaHora(hora);
            if (sliderHora != null)
            {
                sliderHora.minValue = horaAmanecer;
                sliderHora.maxValue = horaAtardecer;
                sliderHora.SetValueWithoutNotify(hora);
            }

            AplicarCamarasActivas(0);
            _vista = 0;
            SituarCamaraInmediato();

            MostrarMenu();
            MostrarFicha(0);
            ActualizarEtiquetaHora(true);
            ActualizarBotones();
            AplicarSol(_noche01);
        }

        private void OnDisable()
        {
            // Devuelve las celdas al SRP Batcher y restaura la iluminación de escena.
            if (renderersCeldas != null && modoFeedback == ModoFeedback.MaterialPropertyBlock)
            {
                for (int i = 0; i < renderersCeldas.Length; i++)
                    if (renderersCeldas[i] != null) renderersCeldas[i].SetPropertyBlock(null);
            }
            RenderSettings.ambientIntensity = _ambienteOriginal;
            RestaurarAtmosfera();
            if (sol != null)
            {
                sol.intensity = _intensidadOriginal;
                sol.colorTemperature = _temperaturaOriginal;
                sol.color = _colorLuzOriginal;
                sol.transform.rotation = _rotacionLuzOriginal;
            }
        }

        private void OnDestroy()
        {
            if (_materialRuntimeCeldas != null) Destroy(_materialRuntimeCeldas);
            if (_materialLed != null) Destroy(_materialLed);
            if (_cieloRuntime != null) Destroy(_cieloRuntime);
            if (_matAzul != null) Destroy(_matAzul);
            if (_matVerde != null) Destroy(_matVerde);
            if (_matRojo != null) Destroy(_matRojo);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _dtSuave = Mathf.Lerp(_dtSuave, dt, 0.05f);

            LeerTeclado();

            if (_autoHora && _estado == Estado.Simulacion)
            {
                hora += horasPorSegundoAuto * dt;
                if (hora > horaAtardecer) hora = horaAmanecer;
                if (sliderHora != null) sliderHora.SetValueWithoutNotify(hora);
            }

            // Transición día/noche
            float objetivoNoche = _modoNoche ? 1f : 0f;
            _noche01 = Mathf.MoveTowards(_noche01, objetivoNoche, dt / Mathf.Max(0.01f, segundosTransicionNoche));

            // Sol
            float objetivo = AnguloSolParaHora(hora);
            _anguloSolActual = Mathf.Lerp(_anguloSolActual, objetivo, 1f - Mathf.Exp(-suavizadoSol * dt));
            AplicarSol(_noche01);

            CalcularTelemetria();
            ActualizarFeedbackCeldas();
            ActualizarLeds();
            ActualizarAcentos();
            AnimarDetalles(dt);
            ActualizarBanner(dt);

            _tUI += dt;
            if (_tUI >= 0.1f)
            {
                _tUI = 0f;
                ActualizarHUD();
            }
            ActualizarEtiquetaHora(false);
        }

        private void LateUpdate()
        {
            ActualizarCamara(Time.unscaledDeltaTime);
        }

        // =================================================================== API de UI (eventos persistentes)
        public void IniciarSimulacion()
        {
            _estado = Estado.Simulacion;
            Activar(panelMenu, false);
            Activar(panelAcerca, false);
            Activar(panelHUD, true);
            Activar(bannerGuia, true);
            _tBanner = 0f;
            Activar(panelFichas, false);
            SetVista(0);
        }

        public void MostrarMenu()
        {
            _estado = Estado.Menu;
            _autoHora = false;
            Activar(panelMenu, true);
            Activar(panelAcerca, false);
            Activar(panelHUD, false);
            Activar(panelFichas, false);
            if (_vista != 0) SetVista(0);
            ActualizarBotones();
        }

        public void VolverAlMenu() { MostrarMenu(); }

        public void MostrarAcerca() { Activar(panelAcerca, true); }
        public void CerrarAcerca() { Activar(panelAcerca, false); }

        public void Salir()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void SetHora(float nuevaHora)
        {
            hora = Mathf.Clamp(nuevaHora, horaAmanecer, horaAtardecer);
        }

        public void AlternarAutoHora()
        {
            _autoHora = !_autoHora;
            ActualizarBotones();
        }

        public void AlternarDiaNoche()
        {
            _modoNoche = !_modoNoche;
            ActualizarBotones();
            ActualizarEtiquetaHora(true);
        }

        public void SetModoNoche(bool noche)
        {
            _modoNoche = noche;
            ActualizarBotones();
            ActualizarEtiquetaHora(true);
        }

        public void CerrarBanner() { Activar(bannerGuia, false); }

        /// <summary>Botón «Más detalles»: despliega / pliega Irradiancia, Hogares, CO2, Elevación…</summary>
        public void AlternarDetalles() { _detallesAbierto = !_detallesAbierto; }
        public void SetDetalles(bool abierto) { _detallesAbierto = abierto; }

        public void AlternarFichas()
        {
            if (panelFichas == null) return;
            Activar(panelFichas, !panelFichas.activeSelf);
        }

        public void CerrarFichas() { Activar(panelFichas, false); }

        public void MostrarFicha(int indice)
        {
            if (fichaTitulos == null || fichaTitulos.Length == 0) return;
            indice = Mathf.Clamp(indice, 0, fichaTitulos.Length - 1);
            if (textoFichaTitulo != null) textoFichaTitulo.text = fichaTitulos[indice];
            if (textoFichaCuerpo != null && fichaCuerpos != null && indice < fichaCuerpos.Length) textoFichaCuerpo.text = fichaCuerpos[indice];
            if (scrollFicha != null) scrollFicha.verticalNormalizedPosition = 1f;
            ResaltarGrupo(botonesFicha, indice);
        }

        public void SetVista(int indice)
        {
            indice = Mathf.Clamp(indice, 0, 2);
            _vista = indice;

            Camera anterior = _camActiva;
            AplicarCamarasActivas(indice);

            _yawOff[indice] = 0f;
            _pitchOff[indice] = 0f;
            _zoom[indice] = 1f;
            if (indice == 2) ReiniciarRecorrido();

            if (_camActiva != null)
            {
                if (anterior == _camActiva)
                {
                    // Misma cámara: viaje suave hacia el nuevo punto de vista.
                    _transDesdePos = _camActiva.transform.position;
                    _transDesdeRot = _camActiva.transform.rotation;
                    _transT = 0f;
                }
                else
                {
                    SituarCamaraInmediato();
                }
            }

            Activar(contenedorJoystick, indice == 2 && _estado == Estado.Simulacion);
            if (indice == 2) Activar(bannerGuia, false);
            ResaltarGrupo(botonesVista, indice);
            if (textoVistaActual != null) textoVistaActual.text = NombresVista[indice];
        }

        // =================================================================== Sol y telemetría
        /// <summary>6:00 → 0° (horizonte este), 12:00 → 90° (cenit), 18:00 → 180° (horizonte oeste).</summary>
        public float AnguloSolParaHora(float h)
        {
            float t = Mathf.InverseLerp(horaAmanecer, horaAtardecer, h);
            return Mathf.Lerp(0f, 180f, t);
        }

        private Quaternion RotacionSol()
        {
            // Rotación en X (recorrido amanecer → atardecer) con azimut fijo en Y.
            Quaternion rot = Quaternion.Euler(_anguloSolActual, azimutSol, 0f);
            if (latitudGrados > 0.01f)
            {
                Vector3 ejeEsteOeste = Quaternion.Euler(0f, azimutSol, 0f) * Vector3.forward;
                rot = Quaternion.AngleAxis(_signoLatitud * latitudGrados, ejeEsteOeste) * rot;
            }
            return rot;
        }

        private void PrepararTrayectoria()
        {
            Vector3 suma = Vector3.zero;
            if (normalesPaneles != null) foreach (Vector3 n in normalesPaneles) suma += n;
            Vector3 frente = new Vector3(suma.x, 0f, suma.z);
            if (frente.sqrMagnitude < 1e-6f) return; // paneles horizontales: se respeta el azimut original
            frente.Normalize();

            if (alinearTrayectoriaConPaneles)
            {
                // Al amanecer la luz viaja de este a oeste: su dirección es perpendicular al frente de los paneles.
                Vector3 haciaOeste = Vector3.Cross(Vector3.up, frente);
                azimutSol = Mathf.Atan2(haciaOeste.x, haciaOeste.z) * Mathf.Rad2Deg;
            }

            // Elige el signo de la inclinación para que al mediodía el sol quede del lado al que miran los paneles.
            _signoLatitud = 1f;
            Vector3 ejeEsteOeste = Quaternion.Euler(0f, azimutSol, 0f) * Vector3.forward;
            Vector3 haciaSolMediodia = -(Quaternion.AngleAxis(latitudGrados, ejeEsteOeste) * Quaternion.Euler(90f, azimutSol, 0f) * Vector3.forward);
            if (Vector3.Dot(haciaSolMediodia, frente) < 0f) _signoLatitud = -1f;
        }

        private void AplicarSol(float noche)
        {
            if (sol == null) return;
            Quaternion rotSol = RotacionSol();
            Quaternion rotLuna = Quaternion.Euler(38f, azimutSol + 180f, 0f);
            sol.transform.rotation = Quaternion.Slerp(rotSol, rotLuna, noche);

            float seno = Mathf.Sin(_anguloSolActual * Mathf.Deg2Rad);
            float intensidadDia = intensidadSolMax * Mathf.Clamp01(0.08f + seno * 1.4f);
            sol.intensity = Mathf.Lerp(intensidadDia, intensidadLuna, noche);

            float temperaturaDia = Mathf.Lerp(2400f, 6300f, Mathf.Sqrt(Mathf.Clamp01(seno)));
            if (sol.useColorTemperature) sol.colorTemperature = Mathf.Lerp(temperaturaDia, temperaturaLuna, noche);
            sol.color = Color.Lerp(_colorLuzOriginal, colorLuna, noche);

            // Elevación del sol (seno) con la inclinación por latitud: gobierna los colores del cielo.
            _elev01 = Mathf.Clamp01(-(RotacionSol() * Vector3.forward).y);
            AplicarAtmosfera(noche);

            if (volumenNocturno != null) volumenNocturno.weight = noche;
        }

        private void CalcularTelemetria()
        {
            // Dirección HACIA el sol (los rayos viajan en sol.forward; usamos la rotación solar pura, no la luna).
            Vector3 haciaSol = -(RotacionSol() * Vector3.forward);
            float senoElev = Mathf.Clamp(haciaSol.y, -1f, 1f);
            ElevacionSolGrados = Mathf.Asin(senoElev) * Mathf.Rad2Deg;

            float factorDia = 1f - _noche01;
            if (senoElev <= 0.0001f || normalesPaneles == null || normalesPaneles.Length == 0)
            {
                EficienciaCaptacion = 0f; IrradianciaPlano = 0f; PotenciaKW = 0f; AnguloIncidenciaGrados = 90f;
                return;
            }

            // Producto punto entre la dirección solar y la normal de cada panel: cos(θ) de incidencia.
            float sumaCos = 0f;
            Vector3 sumaNormales = Vector3.zero;
            for (int i = 0; i < normalesPaneles.Length; i++)
            {
                Vector3 n = normalesPaneles[i];
                sumaCos += Mathf.Max(0f, Vector3.Dot(haciaSol, n));
                sumaNormales += n;
            }
            float cosMedio = sumaCos / normalesPaneles.Length;
            _normalMedia = sumaNormales.sqrMagnitude > 0f ? sumaNormales.normalized : Vector3.up;
            AnguloIncidenciaGrados = Mathf.Acos(Mathf.Clamp(Vector3.Dot(haciaSol, _normalMedia), -1f, 1f)) * Mathf.Rad2Deg;

            // Masa de aire (Kasten–Young) y radiación directa (Meinel): menos energía con el sol bajo.
            float zenit = 90f - ElevacionSolGrados;
            float masaAire = 1f / (Mathf.Cos(zenit * Mathf.Deg2Rad) + 0.50572f * Mathf.Pow(Mathf.Max(0.01f, 96.07995f - zenit), -1.6364f));
            float dni = 1361f * Mathf.Pow(0.7f, Mathf.Pow(masaAire, 0.678f));
            float difusa = 0.1f * dni;

            EficienciaCaptacion = cosMedio * factorDia;
            IrradianciaPlano = (dni * cosMedio + difusa) * factorDia;
            PotenciaKW = CapacidadInstaladaKWp * (IrradianciaPlano / 1000f) * performanceRatio;
        }

        // =================================================================== Feedback de celdas
        private void PrepararCeldas()
        {
            if ((renderersCeldas == null || renderersCeldas.Length == 0) && filas != null)
            {
                var lista = new List<Renderer>();
                foreach (Transform f in filas)
                {
                    if (f == null) continue;
                    foreach (Renderer r in f.GetComponentsInChildren<Renderer>(true))
                        if (EsCelda(r, materialCeldas)) lista.Add(r);
                }
                renderersCeldas = lista.ToArray();
            }
            if (materialCeldas == null && renderersCeldas != null && renderersCeldas.Length > 0 && renderersCeldas[0] != null)
                materialCeldas = renderersCeldas[0].sharedMaterial;

            if (materialCeldas != null && materialCeldas.HasProperty(IdBaseColor))
                _colorBaseCeldas = materialCeldas.GetColor(IdBaseColor);

            _mpb = new MaterialPropertyBlock();

            if (modoFeedback == ModoFeedback.MaterialCompartidoRuntime && materialCeldas != null && renderersCeldas != null)
            {
                // Una sola instancia creada una vez (no por renderer): el SRP Batcher se conserva.
                _materialRuntimeCeldas = new Material(materialCeldas) { name = materialCeldas.name + " (SPT Runtime)" };
                _materialRuntimeCeldas.EnableKeyword("_EMISSION");
                foreach (Renderer r in renderersCeldas)
                {
                    if (r == null) continue;
                    Material[] mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) if (mats[i] == materialCeldas) mats[i] = _materialRuntimeCeldas;
                    r.sharedMaterials = mats;
                }
            }

            if (modoFeedback == ModoFeedback.MaterialPropertyBlock && materialCeldas != null && !materialCeldas.IsKeywordEnabled("_EMISSION"))
                Debug.LogWarning("[Sol Para Todos] El material de celdas no tiene _EMISSION activo: el brillo no se verá. Ejecuta Tools > Sol Para Todos > Construir Escena Completa y UI.", materialCeldas);
        }

        private void ActualizarFeedbackCeldas()
        {
            if (modoFeedback == ModoFeedback.Desactivado || renderersCeldas == null || renderersCeldas.Length == 0) return;

            float fraccion = Mathf.Clamp01(IrradianciaPlano / 1000f);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(umbralInicioBrillo, 0.95f, fraccion));
            k = Mathf.Round(k * 48f) / 48f; // cuantizado: evita re-aplicar por variaciones mínimas

            if (!Mathf.Approximately(k, _brilloObjetivo) || _brilloAplicado < 0f)
            {
                _brilloObjetivo = k;
                _pendientesMpb = renderersCeldas.Length;
                _brilloAplicado = k;
            }

            Color emision = colorEnergia * (emisionMax * _brilloObjetivo);
            Color baseTintada = Color.Lerp(_colorBaseCeldas, colorEnergia, tinteMax * _brilloObjetivo);
            baseTintada.a = _colorBaseCeldas.a;

            if (modoFeedback == ModoFeedback.MaterialCompartidoRuntime)
            {
                if (_materialRuntimeCeldas != null && _pendientesMpb > 0)
                {
                    _materialRuntimeCeldas.SetColor(IdEmission, emision);
                    _materialRuntimeCeldas.SetColor(IdBaseColor, baseTintada);
                    _pendientesMpb = 0;
                }
                return;
            }

            if (_pendientesMpb <= 0) return;

            bool quitarBloque = _brilloObjetivo <= 0.001f;
            if (!quitarBloque)
            {
                _mpb.Clear();
                _mpb.SetColor(IdEmission, emision);
                _mpb.SetColor(IdBaseColor, baseTintada);
            }

            int n = renderersCeldas.Length;
            int lote = Mathf.Min(Mathf.Max(64, renderersPorFrame), _pendientesMpb);
            for (int i = 0; i < lote; i++)
            {
                Renderer r = renderersCeldas[_cursorMpb];
                if (r != null)
                {
                    // Sin brillo: se retira el bloque y el renderer vuelve a ser compatible con el SRP Batcher.
                    if (quitarBloque) r.SetPropertyBlock(null);
                    else r.SetPropertyBlock(_mpb);
                }
                _cursorMpb = (_cursorMpb + 1) % n;
            }
            _pendientesMpb -= lote;
        }

        // =================================================================== Indicadores (inversores)
        private void PrepararLeds()
        {
            if (ledsIndicadores == null || ledsIndicadores.Length == 0 || ledsIndicadores[0] == null) return;
            Material baseLed = ledsIndicadores[0].sharedMaterial;
            if (baseLed == null) return;
            _materialLed = new Material(baseLed) { name = baseLed.name + " (SPT Runtime)" };
            _materialLed.EnableKeyword("_EMISSION");
            foreach (Renderer r in ledsIndicadores) if (r != null) r.sharedMaterial = _materialLed;
        }

        private void ActualizarLeds()
        {
            if (_materialLed == null) return;
            bool generando = PotenciaKW > 1f;
            Color c = generando ? colorLedGenerando : colorLedEspera;
            float pulso = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * (generando ? 2.2f : 4.5f));
            float intensidad = Mathf.Lerp(0.6f, 2.2f + 2.0f * pulso, _noche01);
            _materialLed.SetColor(IdEmission, c * intensidad);
            _materialLed.SetColor(IdBaseColor, Color.Lerp(c, Color.black, 0.35f));
        }

        // =================================================================== HUD
        private void ActualizarHUD()
        {
            if (_estado != Estado.Simulacion) return;

            if (textoPotencia != null)
            {
                if (PotenciaKW >= 1000f) textoPotencia.SetText("{0:2} MW", PotenciaKW / 1000f);
                else textoPotencia.SetText("{0:1} kW", PotenciaKW);
            }
            if (textoEficiencia != null) textoEficiencia.SetText("{0:1} %", EficienciaCaptacion * 100f);
            if (barraEficiencia != null)
            {
                _barraSuave = Mathf.Lerp(_barraSuave, EficienciaCaptacion, 0.45f);
                barraEficiencia.fillAmount = _barraSuave;
                barraEficiencia.color = colorBarra;
            }
            if (textoIrradiancia != null) textoIrradiancia.SetText("{0:0} W/m²", IrradianciaPlano);
            if (textoIncidencia != null) textoIncidencia.SetText("{0:1}°", AnguloIncidenciaGrados);
            if (textoElevacion != null) textoElevacion.SetText("{0:1}°", Mathf.Max(0f, ElevacionSolGrados));
            if (textoHogares != null) textoHogares.SetText("{0:0}", consumoHogarKW > 0f ? PotenciaKW / consumoHogarKW : 0f);
            if (textoCO2 != null) textoCO2.SetText("{0:0} kg/h", PotenciaKW * factorEmisionKgPorKWh);
            if (textoCapacidad != null) textoCapacidad.SetText("{0:0} kWp instalados", CapacidadInstaladaKWp);
            if (textoFPS != null) textoFPS.SetText("{0:0} FPS", 1f / Mathf.Max(0.0001f, _dtSuave));

            if (textoEstado != null)
            {
                if (_noche01 > 0.5f) textoEstado.text = "<color=#4FC3F7>•</color> Crepúsculo — inspección nocturna";
                else if (ElevacionSolGrados <= 0.5f) textoEstado.text = "<color=#4FC3F7>•</color> Sol en el horizonte — sin generación";
                else if (_brilloObjetivo > 0.5f) textoEstado.text = "<color=#3DFF8A>•</color> Radiación pico — máxima captación";
                else textoEstado.text = "<color=#3DFF8A>•</color> Generando — inversores en línea";
            }
        }

        private void ActualizarEtiquetaHora(bool forzar)
        {
            int minutos = Mathf.FloorToInt(hora * 60f + 0.5f);
            if (!forzar && minutos == _ultimoMinutoMostrado) return;
            _ultimoMinutoMostrado = minutos;
            int h24 = (minutos / 60) % 24;
            int m = minutos % 60;
            int h12 = h24 % 12; if (h12 == 0) h12 = 12;
            // Espacios de no separación + «a. m.» más pequeño: el reloj nunca se parte en dos líneas.
            string sufijo = h24 < 12 ? "a.\u00A0m." : "p.\u00A0m.";
            if (textoHora != null) textoHora.text = string.Format("{0}:{1:00}\u00A0<size=46%>{2}</size>", h12, m, sufijo);
            if (textoFaseDia != null)
            {
                string fase;
                if (hora < 7.5f) fase = "AMANECER";
                else if (hora < 10.5f) fase = "MAÑANA";
                else if (hora < 13.5f) fase = "MEDIODÍA · HORAS PICO";
                else if (hora < 16.5f) fase = "TARDE";
                else fase = "ATARDECER";
                textoFaseDia.text = _modoNoche ? "CREPÚSCULO · LABORATORIO" : fase;
            }
        }

        private void ActualizarBotones()
        {
            if (textoBotonDiaNoche != null) textoBotonDiaNoche.text = _modoNoche ? "Modo Día" : "Modo Noche";
            if (imagenIconoNoche != null)
            {
                Sprite ico = _modoNoche ? spriteSol : spriteLuna;
                if (ico != null) imagenIconoNoche.sprite = ico;
            }
            if (textoBotonAuto != null) textoBotonAuto.text = _autoHora ? "Pausa" : "Auto";
        }

        private void ResaltarGrupo(Button[] botones, int activo)
        {
            if (botones == null) return;
            for (int i = 0; i < botones.Length; i++)
            {
                if (botones[i] == null) continue;
                Graphic g = botones[i].targetGraphic;
                if (g != null) g.color = i == activo ? colorBotonActivo : colorBotonNormal;
                TMP_Text etiqueta = botones[i].GetComponentInChildren<TMP_Text>(true);
                if (etiqueta != null) etiqueta.color = i == activo ? colorTextoActivo : Color.white;
            }
        }

        // =================================================================== Atmósfera (cielo, niebla, ambiente)
        private void PrepararAtmosfera()
        {
            if (!controlarAtmosfera) return;
            _ambModoOrig = RenderSettings.ambientMode;
            _ambSkyOrig = RenderSettings.ambientSkyColor;
            _ambEqOrig = RenderSettings.ambientEquatorColor;
            _ambGroundOrig = RenderSettings.ambientGroundColor;
            _fogOrig = RenderSettings.fog;
            _fogColorOrig = RenderSettings.fogColor;
            _fogModoOrig = RenderSettings.fogMode;
            _fogIniOrig = RenderSettings.fogStartDistance;
            _fogFinOrig = RenderSettings.fogEndDistance;
            _cieloOriginal = RenderSettings.skybox;
            _ambienteGuardado = true;

            if (materialCielo != null)
            {
                _cieloRuntime = new Material(materialCielo) { name = materialCielo.name + " (SPT Runtime)" };
                RenderSettings.skybox = _cieloRuntime;
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = nieblaInicio;
            RenderSettings.fogEndDistance = nieblaFin;
        }

        private void RestaurarAtmosfera()
        {
            if (!_ambienteGuardado) return;
            RenderSettings.ambientMode = _ambModoOrig;
            RenderSettings.ambientSkyColor = _ambSkyOrig;
            RenderSettings.ambientEquatorColor = _ambEqOrig;
            RenderSettings.ambientGroundColor = _ambGroundOrig;
            RenderSettings.fog = _fogOrig;
            RenderSettings.fogColor = _fogColorOrig;
            RenderSettings.fogMode = _fogModoOrig;
            RenderSettings.fogStartDistance = _fogIniOrig;
            RenderSettings.fogEndDistance = _fogFinOrig;
            RenderSettings.skybox = _cieloOriginal;
        }

        private void AplicarAtmosfera(float noche)
        {
            if (!controlarAtmosfera || !_ambienteGuardado) return;

            // Solo recalcula cuando algo cambió de verdad (evita reescribir RenderSettings y el material cada frame).
            float ang = _anguloSolActual;
            if (_atmValida && Mathf.Abs(noche - _atmNoche) < 0.002f && Mathf.Abs(_elev01 - _atmElev) < 0.0015f && Mathf.Abs(ang - _atmAngulo) < 0.02f) return;
            _atmValida = true; _atmNoche = noche; _atmElev = _elev01; _atmAngulo = ang;

            // Amanecer/atardecer cálidos → cielo azul de mediodía (k según la elevación del sol).
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_elev01 / 0.5f));
            Color zenitDia = Color.Lerp(cieloZenitAmanecer, cieloZenitDia, k);
            Color horizDia = Color.Lerp(cieloHorizonteAmanecer, cieloHorizonteDia, k);
            Color zenit = Color.Lerp(zenitDia, cieloZenitNoche, noche);
            Color horiz = Color.Lerp(horizDia, cieloHorizonteNoche, noche);

            // Ambiente Trilight: de día se atenúa con el sol bajo; de noche es azul oscuro (la silueta no se pierde).
            float factorDia = intensidadAmbienteDia * Mathf.Lerp(0.55f, 1f, k);
            Color aSky = Color.Lerp(ambienteCieloDia * factorDia, ambienteCieloNoche * ambienteNoche, noche);
            Color aEq = Color.Lerp(ambienteEcuadorDia * factorDia, ambienteEcuadorNoche * ambienteNoche, noche);
            Color aGr = Color.Lerp(ambienteSueloDia * factorDia, ambienteSueloNoche * ambienteNoche, noche);
            aSky.a = aEq.a = aGr.a = 1f;
            RenderSettings.ambientSkyColor = aSky;
            RenderSettings.ambientEquatorColor = aEq;
            RenderSettings.ambientGroundColor = aGr;
            RenderSettings.ambientIntensity = 1f;

            // La niebla toma el color del horizonte del cielo: el terreno se funde sin línea de horizonte.
            RenderSettings.fogColor = horiz;
            RenderSettings.fogStartDistance = nieblaInicio;
            RenderSettings.fogEndDistance = nieblaFin;

            if (_cieloRuntime != null)
            {
                Vector3 haciaAstro = sol != null ? -sol.transform.forward : Vector3.up;
                Color colorAstro = Color.Lerp(Color.Lerp(new Color(1f, 0.55f, 0.28f), new Color(1f, 0.95f, 0.85f), k), new Color(0.82f, 0.9f, 1f), noche);
                // Las propiedades Color de un material se interpretan en gamma: se pasan en lineal (espacio de color Linear).
                _cieloRuntime.SetColor("_Zenith", zenit.linear);
                _cieloRuntime.SetColor("_Horizon", horiz.linear);
                _cieloRuntime.SetColor("_GroundCol", Color.Lerp(horiz, Color.black, 0.35f).linear);
                _cieloRuntime.SetColor("_SunColor", colorAstro.linear);
                _cieloRuntime.SetVector("_SunDir", new Vector4(haciaAstro.x, haciaAstro.y, haciaAstro.z, 0f));
                _cieloRuntime.SetFloat("_SunSize", Mathf.Lerp(1.4f, 2.4f, noche));
                _cieloRuntime.SetFloat("_Night", noche);
            }
        }

        // =================================================================== Indicadores luminosos (acentos)
        private Material CopiarMaterialGrupo(Renderer[] grupo, string sufijo)
        {
            if (grupo == null || grupo.Length == 0) return null;
            Renderer primero = null;
            foreach (Renderer r in grupo) if (r != null) { primero = r; break; }
            if (primero == null || primero.sharedMaterial == null) return null;
            var m = new Material(primero.sharedMaterial) { name = primero.sharedMaterial.name + " (SPT " + sufijo + ")" };
            m.EnableKeyword("_EMISSION");
            foreach (Renderer r in grupo) if (r != null) r.sharedMaterial = m;
            return m;
        }

        private void PrepararAcentos()
        {
            _matAzul = CopiarMaterialGrupo(acentosAzul, "azul");
            _matVerde = CopiarMaterialGrupo(acentosVerde, "verde");
            _matRojo = CopiarMaterialGrupo(acentosRojo, "rojo");
        }

        private void ActualizarAcentos()
        {
            if (_matAzul == null && _matVerde == null && _matRojo == null) return;
            float baseI = Mathf.Lerp(emisionAcentoDia, emisionAcentoNoche, _noche01);
            float t = Time.unscaledTime;
            if (_matAzul != null)
            {
                float i = baseI * (1f + 0.10f * _noche01 * Mathf.Sin(t * 1.7f));
                _matAzul.SetColor(IdEmission, colorAcentoAzul * i);
            }
            if (_matVerde != null)
            {
                float i = baseI * (1f + 0.15f * _noche01 * Mathf.Sin(t * 2.3f + 1.2f));
                _matVerde.SetColor(IdEmission, colorAcentoVerde * i);
            }
            if (_matRojo != null)
            {
                // Baliza de obstáculo: parpadeo lento.
                float parpadeo = 0.45f + 0.55f * Mathf.SmoothStep(0f, 1f, 0.5f + 0.5f * Mathf.Sin(t * 2.6f));
                _matRojo.SetColor(IdEmission, colorAcentoRojo * (baseI * parpadeo));
            }
        }

        // =================================================================== HUD (v2): detalles y banner
        private void AnimarDetalles(float dt)
        {
            if (panelDetalles == null) return;
            _detallesT = Mathf.MoveTowards(_detallesT, _detallesAbierto ? 1f : 0f, dt / 0.22f);
            float e = Mathf.SmoothStep(0f, 1f, _detallesT);
            bool visible = e > 0.001f;
            if (panelDetalles.gameObject.activeSelf != visible) panelDetalles.gameObject.SetActive(visible);
            if (!visible)
            {
                if (iconoDetalles != null) iconoDetalles.localRotation = Quaternion.identity;
                return;
            }
            panelDetalles.localScale = new Vector3(1f, e, 1f);
            if (grupoDetalles != null)
            {
                grupoDetalles.alpha = e;
                grupoDetalles.blocksRaycasts = e > 0.9f;
            }
            if (iconoDetalles != null) iconoDetalles.localRotation = Quaternion.Euler(0f, 0f, 180f * e);
        }

        private void ActualizarBanner(float dt)
        {
            if (bannerGuia == null || !bannerGuia.activeSelf || segundosBanner <= 0f) return;
            _tBanner += dt;
            if (_tBanner >= segundosBanner) CerrarBanner();
        }

        // =================================================================== Cámaras
        private void PrepararAnclas()
        {
            for (int i = 0; i < 3; i++)
            {
                Transform t = null;
                if (modoCamaras == ModoCamaras.CambiarEntreCamaras && camarasEscena != null && i < camarasEscena.Length && camarasEscena[i] != null)
                    t = camarasEscena[i].transform;
                else if (vistas != null && i < vistas.Length && vistas[i] != null)
                    t = vistas[i];
                else if (camaraPrincipal != null)
                    t = camaraPrincipal.transform;

                _anclas[i] = t != null ? new Pose(t.position, t.rotation) : new Pose(Vector3.up * 10f, Quaternion.identity);
            }
            if (distanciaOrbita == null || distanciaOrbita.Length < 3) distanciaOrbita = new[] { 80f, 6f, 0f };
        }

        private void AplicarCamarasActivas(int indice)
        {
            Camera objetivo = camaraPrincipal;
            if (modoCamaras == ModoCamaras.CambiarEntreCamaras && camarasEscena != null && indice < camarasEscena.Length && camarasEscena[indice] != null)
                objetivo = camarasEscena[indice];
            if (objetivo == null) objetivo = Camera.main;

            // Solo una cámara renderiza y solo ella escucha audio: sin doble costo ni avisos de AudioListener.
            var todas = new List<Camera>();
            if (camaraPrincipal != null) todas.Add(camaraPrincipal);
            if (camarasEscena != null) foreach (Camera c in camarasEscena) if (c != null && !todas.Contains(c)) todas.Add(c);
            foreach (Camera c in todas)
            {
                bool activa = c == objetivo;
                c.enabled = activa;
                AudioListener al = c.GetComponent<AudioListener>();
                if (al != null) al.enabled = activa;
            }
            _camActiva = objetivo;
        }

        private void ReiniciarRecorrido()
        {
            Pose a = _anclas[2];
            _recorridoPos = a.position;
            Vector3 e = a.rotation.eulerAngles;
            _recorridoYaw = e.y;
            _recorridoPitch = NormalizarAngulo(e.x);
        }

        private void SituarCamaraInmediato()
        {
            if (_camActiva == null) return;
            Vector3 p; Quaternion r;
            PoseObjetivo(_vista, out p, out r);
            _camActiva.transform.SetPositionAndRotation(p, r);
            _transT = 1f;
        }

        private void PoseObjetivo(int v, out Vector3 pos, out Quaternion rot)
        {
            if (v == 2)
            {
                pos = _recorridoPos;
                rot = Quaternion.Euler(_recorridoPitch, _recorridoYaw, 0f);
                return;
            }
            Pose a = _anclas[v];
            float distBase = Mathf.Max(0.5f, distanciaOrbita[v]);
            Vector3 pivote = a.position + a.rotation * Vector3.forward * distBase;
            Vector3 e = a.rotation.eulerAngles;
            float pitch = Mathf.Clamp(NormalizarAngulo(e.x) + _pitchOff[v], v == 0 ? 8f : -15f, 85f);
            float yaw = e.y + _yawOff[v];
            rot = Quaternion.Euler(pitch, yaw, 0f);
            pos = pivote - rot * Vector3.forward * (distBase * _zoom[v]);
        }

        private void ActualizarCamara(float dt)
        {
            if (_camActiva == null) return;

            Vector2 arrastre = areaMirada != null ? areaMirada.ConsumirArrastre() : Vector2.zero;
            float pellizco = areaMirada != null ? areaMirada.ConsumirPellizco() : 0f;
            float escala = 1080f / Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height));
            Vector2 giro = arrastre * sensibilidadMirada * escala;

            Vector2 mover = Vector2.zero;
            if (joystick != null) mover = joystick.Valor;
            float rueda = 0f;
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) mover.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) mover.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) mover.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) mover.x -= 1f;
            }
            Mouse mouse = Mouse.current;
            if (mouse != null) rueda = mouse.scroll.ReadValue().y;
#endif
            mover = Vector2.ClampMagnitude(mover, 1f);

            if (_estado == Estado.Menu)
            {
                _yawOff[0] += velocidadOrbitaMenu * dt;
            }
            else if (_transT >= 1f)
            {
                if (_vista == 2)
                {
                    _recorridoYaw += giro.x;
                    _recorridoPitch = Mathf.Clamp(_recorridoPitch - giro.y, -60f, 60f);
                    Vector3 adelante = Quaternion.Euler(0f, _recorridoYaw, 0f) * Vector3.forward;
                    Vector3 derecha = Quaternion.Euler(0f, _recorridoYaw, 0f) * Vector3.right;
                    _recorridoPos += (adelante * mover.y + derecha * mover.x) * velocidadRecorrido * dt;
                    if (limitesRecorrido.size.sqrMagnitude > 0.01f)
                    {
                        Vector3 min = limitesRecorrido.min, max = limitesRecorrido.max;
                        _recorridoPos.x = Mathf.Clamp(_recorridoPos.x, min.x, max.x);
                        _recorridoPos.z = Mathf.Clamp(_recorridoPos.z, min.z, max.z);
                    }
                }
                else
                {
                    _yawOff[_vista] += giro.x;
                    _pitchOff[_vista] -= giro.y;
                    float zoom = _zoom[_vista] * (1f - pellizco * 0.0025f - rueda * 0.0008f);
                    _zoom[_vista] = Mathf.Clamp(zoom, 0.35f, 2.2f);
                }
            }

            Vector3 pObj; Quaternion rObj;
            PoseObjetivo(_vista, out pObj, out rObj);

            Transform ct = _camActiva.transform;
            if (_transT < 1f)
            {
                _transT = Mathf.Min(1f, _transT + dt / Mathf.Max(0.05f, duracionTransicion));
                float s = Mathf.SmoothStep(0f, 1f, _transT);
                ct.SetPositionAndRotation(Vector3.Lerp(_transDesdePos, pObj, s), Quaternion.Slerp(_transDesdeRot, rObj, s));
            }
            else
            {
                float a = 1f - Mathf.Exp(-14f * dt);
                ct.SetPositionAndRotation(Vector3.Lerp(ct.position, pObj, a), Quaternion.Slerp(ct.rotation, rObj, a));
            }
        }

        private void LeerTeclado()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            // En Android el botón "atrás" llega como Escape.
            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (panelFichas != null && panelFichas.activeSelf) CerrarFichas();
                else if (panelAcerca != null && panelAcerca.activeSelf) CerrarAcerca();
                else if (_estado == Estado.Simulacion) VolverAlMenu();
            }
            if (_estado != Estado.Simulacion) return;
            if (kb.digit1Key.wasPressedThisFrame) SetVista(0);
            if (kb.digit2Key.wasPressedThisFrame) SetVista(1);
            if (kb.digit3Key.wasPressedThisFrame) SetVista(2);
            if (kb.nKey.wasPressedThisFrame) AlternarDiaNoche();
#endif
        }

        // =================================================================== Utilidades
        private void PrepararNormales()
        {
            if (normalesPaneles != null && normalesPaneles.Length > 0) return;
            normalesPaneles = CalcularNormales(filas, materialCeldas, out modulosPorUnidad);
        }

        /// <summary>Calcula una normal por unidad Panel_LOD (LODGroup) a partir de sus celdas en LOD0.</summary>
        public static Vector3[] CalcularNormales(Transform[] raices, Material materialCelda, out int modulosPorUnidad)
        {
            var normales = new List<Vector3>();
            modulosPorUnidad = 0;
            if (raices == null) return normales.ToArray();

            foreach (Transform raiz in raices)
            {
                if (raiz == null) continue;
                foreach (LODGroup g in raiz.GetComponentsInChildren<LODGroup>(true))
                {
                    LOD[] lods = g.GetLODs();
                    if (lods == null || lods.Length == 0) continue;
                    Vector3 suma = Vector3.zero;
                    int celdas = 0;
                    foreach (Renderer r in lods[0].renderers)
                    {
                        if (r == null || !EsCelda(r, materialCelda)) continue;
                        suma += NormalDesdeRenderer(r);
                        celdas++;
                    }
                    if (celdas == 0) continue;
                    normales.Add(suma.normalized);
                    if (celdas > modulosPorUnidad) modulosPorUnidad = celdas;
                }
            }
            if (modulosPorUnidad == 0) modulosPorUnidad = 1;
            return normales.ToArray();
        }

        /// <summary>Normal de una placa delgada: el eje local más delgado de sus bounds, llevado a mundo y orientado hacia arriba.</summary>
        public static Vector3 NormalDesdeRenderer(Renderer r)
        {
            Bounds b = r.localBounds;
            Vector3 s = b.size;
            Vector3 eje = s.x <= s.y && s.x <= s.z ? Vector3.right : (s.y <= s.z ? Vector3.up : Vector3.forward);
            Vector3 n = r.transform.TransformDirection(eje).normalized;
            if (n.y < 0f) n = -n;
            return n;
        }

        public static bool EsCelda(Renderer r, Material materialCelda)
        {
            if (r == null) return false;
            string nombre = r.name;
            if (nombre.IndexOf("Fotovolt", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (materialCelda != null)
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) if (mats[i] == materialCelda) return true;
            }
            return false;
        }

        private static float NormalizarAngulo(float a)
        {
            a %= 360f;
            if (a > 180f) a -= 360f;
            if (a < -180f) a += 360f;
            return a;
        }

        private static void Activar(GameObject go, bool activo)
        {
            if (go != null && go.activeSelf != activo) go.SetActive(activo);
        }

        private static Light BuscarLuzDireccional()
        {
            Light l = RenderSettings.sun;
            if (l != null) return l;
            foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (Light c in root.GetComponentsInChildren<Light>(true))
                    if (c.type == LightType.Directional) return c;
            return null;
        }

        // =================================================================== Contenido por defecto
        public static readonly string[] FichasTitulosPorDefecto =
        {
            "Celdas Fotovoltaicas",
            "Estructura y Soporte",
            "Optimización Gráfica (LOD & URP)"
        };

        public static readonly string[] FichasCuerposPorDefecto =
        {
            "<b>¿Cómo se convierte la luz en electricidad?</b>\n" +
            "Cada módulo está formado por celdas de silicio cristalino. Cuando un fotón con energía suficiente incide sobre la unión P-N, libera un electrón y se genera una corriente continua: es el <b>efecto fotovoltaico</b>. Los inversores convierten esa corriente a alterna para la red.\n\n" +
            "<b>El ángulo lo es todo</b>\n" +
            "La potencia captada es proporcional al coseno del ángulo entre los rayos solares y la normal del panel. En la simulación se calcula con el producto punto <i>Vector3.Dot(dirSol, normal)</i>: con incidencia perpendicular el coseno vale 1 (100 %); a 60° solo se aprovecha la mitad.\n\n" +
            "<b>Reducción de pérdidas ópticas</b>\n" +
            "· Vidrio templado con recubrimiento antirreflejante: más luz llega a la celda.\n" +
            "· Texturizado de la superficie del silicio para «atrapar» la luz.\n" +
            "· Limpieza periódica: el polvo puede restar varios puntos de rendimiento.\n" +
            "· Separación entre filas calculada para evitar sombras mutuas al amanecer y atardecer.\n\n" +
            "<b>Datos de la simulación</b>\n" +
            "Se asume un módulo de 550 Wp y un Performance Ratio de 0.80 (temperatura, cableado e inversor). La irradiancia directa se atenúa con la masa de aire: con el sol bajo, la luz atraviesa más atmósfera y llega menos energía.",

            "<b>Cimentación de concreto</b>\n" +
            "Los pilares de concreto anclan la estructura al terreno y resisten cargas de viento y peso propio. Su altura mantiene los módulos lejos de la humedad, la vegetación y el polvo del suelo.\n\n" +
            "<b>Perfiles de aluminio</b>\n" +
            "Marcos, soportes y tubos de aluminio: ligeros, resistentes a la corrosión y reciclables. Forman un bastidor rígido que mantiene el ángulo de inclinación durante décadas.\n\n" +
            "<b>Orientación estratégica</b>\n" +
            "En el hemisferio norte los paneles miran hacia el sur con una inclinación cercana a la latitud del sitio (Mérida, aprox. 21°). Así el promedio anual del ángulo de incidencia es mínimo y la captación máxima.\n\n" +
            "<b>Distribución en filas</b>\n" +
            "La distancia entre filas equilibra dos objetivos: aprovechar el terreno y evitar que una fila sombree a la siguiente. Usa la <b>Vista 3</b> para recorrer los pasillos entre pilares, tubos y soportes.",

            "<b>Niveles de detalle (LOD Group)</b>\n" +
            "Cada unidad Panel_LOD tiene tres versiones: <b>LOD0</b> (malla completa, vista cercana), <b>LOD1</b> (reducida por Planar a 10°) y <b>LOD2</b> (reducida a 20°). Unity elige el nivel según el tamaño en pantalla, con <i>LOD Cross Fade</i> para que el cambio no se note. Compruébalo en la <b>Vista 1</b>.\n\n" +
            "<b>Filtrado anisotrópico y Mip Maps</b>\n" +
            "Las texturas de las celdas tienen patrones finos y repetidos; vistas en ángulo rasante generan efecto Moiré. Los Mip Maps y el Aniso Level lo eliminan sin costo apreciable.\n\n" +
            "<b>SRP Batcher</b>\n" +
            "Todas las unidades comparten los mismos materiales URP, así que el SRP Batcher agrupa cientos de paneles reutilizando los datos del material en la GPU. El brillo de las celdas usa <i>MaterialPropertyBlock</i>: no se crea ni un material nuevo por panel.\n\n" +
            "<b>Configuración móvil (Mobile_RPAsset)</b>\n" +
            "MSAA 4x para bordes limpios en la estructura metálica, Render Scale 1.0 para nitidez nativa e IL2CPP ARM64 para el mejor rendimiento en Android."
        };
    }
}
