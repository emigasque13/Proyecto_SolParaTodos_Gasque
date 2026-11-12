// ============================================================================
//  Sol Para Todos — Constructor automático de escena y UI (Editor)  ·  v2
//  Menú: Tools > Sol Para Todos > Construir Escena Completa y UI
//
//  Qué hace (idempotente: puedes ejecutarlo las veces que quieras):
//   1. Verifica/importa TMP Essential Resources y crea las fuentes Barlow (TMP SDF).
//   2. Encuentra Directional Light, Main Camera (2, 3), Global Volume y los paneles solares.
//   3. GRANJA: reordena los paneles existentes en una rejilla simétrica (4 bloques, calles
//      centrales, inversores en el cruce), clonando o quitando unidades según el tamaño elegido.
//      No se modifica ningún prefab: solo posición / instanciación de las instancias de la escena.
//   4. TERRENO: el «Pasto» pasa a ser un disco enorme con UV de mosaico + niebla lineal y cielo
//      en degradado cuyo color de horizonte = color de niebla (sin línea de horizonte).
//   5. Activa _EMISSION en el material compartido de las celdas (feedback MPB), crea 3 vistas,
//      inversores con LED, indicadores emisivos (azul / verde / rojo) y el Volume nocturno.
//   6. Construye la UI (Canvas 1080x1920 / 1920x1080): menú plano, HUD translúcido estilo
//      Tesla/SpaceX, «Más detalles» desplegable, reloj sin deformación, fichas y joystick.
//   7. Conecta todo al SolarFieldController con UnityEventTools.AddPersistentListener.
//
//  Solo borra lo que tenga el componente SPTGenerado (creado por este mismo script). Los paneles
//  originales se reutilizan; las «Fila N» antiguas (vacías) se sustituyen por «Fila 01…».
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SolParaTodos;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class SolParaTodosSceneBuilder
{
    private const string Menu = "Tools/Sol Para Todos/";
    private const string CarpetaRaiz = "Assets/SolParaTodos";
    private const string CarpetaMateriales = "Assets/SolParaTodos/Materiales";
    private const string CarpetaSettings = "Assets/SolParaTodos/Settings";
    private const string CarpetaMallas = "Assets/SolParaTodos/Mallas";
    private const string CarpetaFuentes = "Assets/SolParaTodos/Fuentes";
    private const string CarpetaSprites = "Assets/SolParaTodos/UI/Sprites";
    private const string RutaGuia = "Assets/SolParaTodos/LEEME_SolParaTodos.md";
    private const string RutaRespaldoURP = "Assets/SolParaTodos/Settings/SPT_URP_Respaldo.txt";
    private const int CapaUI = 5;

    // ----------------------------------------------------------------- Parámetros de la granja
    private const string ClavePreset = "SPT_PresetGranja";
    private const float GapPasillo = 2.8f;     // m entre filas (pasillo de mantenimiento)
    private const float GapUnidad = 0.30f;     // m entre unidades de una misma fila
    private const float CarreteraX = 16f;      // calle central (camino + banquetas con inversores) entre los bloques oeste / este
    private const float CarreteraZ = 0.30f;    // sin calle E–O: las filas son largas y continuas, como en la foto
    private const float MargenPerimetro = 16f; // distancia del camino perimetral al borde de la granja
    private const float RadioTerreno = 2600f;  // m
    private const float TamMosaicoPasto = 3.5f; // m por repetición de la textura de pasto (más chico = césped más natural)
    private const float NieblaInicio = 220f;
    private const float NieblaFin = 1100f;

    private struct PresetGranja
    {
        public string nombre;
        public int filasPorBloque;     // filas (columnas en X) por bloque
        public int unidadesPorBloque;  // unidades por fila y por bloque
        public int Total { get { return filasPorBloque * 2 * unidadesPorBloque * 2; } }
    }

    private static readonly PresetGranja[] Presets =
    {
        new PresetGranja { nombre = "Compacta (192 unidades)", filasPorBloque = 8, unidadesPorBloque = 6 },
        new PresetGranja { nombre = "Estándar (308 unidades)", filasPorBloque = 11, unidadesPorBloque = 7 },
        new PresetGranja { nombre = "Grande (480 unidades)", filasPorBloque = 15, unidadesPorBloque = 8 }
    };

    private sealed class GeoGranja
    {
        public bool valida;
        public Transform campo;
        public Transform[] filas = new Transform[0];
        public float[] xs = new float[0];   // centro X de cada fila (espacio local de Campo)
        public float[] zs = new float[0];   // centro Z de cada unidad
        public Vector2 huella;              // tamaño (X, Z) de una unidad
        public float anchoX, largoZ;        // extensión total de la granja
        public float sueloY;
        public int unidades;
    }

    // ----------------------------------------------------------------- Paleta v2 (HUD estilo Tesla / SpaceX)
    private static readonly Color CVidrio = new Color32(8, 11, 16, 178);        // ≈70 % opaco
    private static readonly Color CVidrioSuave = new Color32(8, 11, 16, 140);   // ≈55 %
    private static readonly Color CVidrioFuerte = new Color32(7, 10, 15, 224);  // ≈88 % (ventanas modales)
    private static readonly Color CBordeCyan = new Color32(0, 209, 255, 82);    // borde fino
    private static readonly Color CBordeNeutro = new Color32(255, 255, 255, 40);
    private static readonly Color CVelo = new Color32(2, 5, 9, 150);
    private static readonly Color CCyan = new Color32(0, 209, 255, 255);
    private static readonly Color CNaranja = new Color32(255, 122, 26, 255);   // solo barras de progreso e iconos
    private static readonly Color CTexto = new Color32(240, 244, 248, 255);
    private static readonly Color CTextoSec = new Color32(150, 166, 184, 255);
    private static readonly Color CTextoTenue = new Color32(108, 122, 138, 255);
    private static readonly Color CTextoOscuro = new Color32(8, 13, 20, 255);
    private static readonly Color CBlanco = new Color32(246, 248, 251, 255);

    private enum Peso { Ligera = 0, Normal = 1, Media = 2, Seminegrita = 3, Negrita = 4 }

    private static Sprite _sprVidrio, _sprBorde, _sprSombra, _sprCirculo, _sprAroCirculo, _sprPastilla, _sprDegradado;
    private static Sprite _sprLuna, _sprSol, _sprMenu, _sprChevron, _sprInfo;
    private static readonly TMP_FontAsset[] _fuentes = new TMP_FontAsset[5];
    private static TMP_FontAsset _fuenteBase;
    private static bool _reintentoPendiente;

    // =================================================================== Menú principal
    [MenuItem(Menu + "Construir Escena Completa y UI", false, 0)]
    public static void Construir()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Sol Para Todos", "Sal del modo Play antes de construir la escena.", "OK");
            return;
        }
        if (!AsegurarTMP()) return;

        Scene escena = SceneManager.GetActiveScene();
        if (!escena.IsValid())
        {
            EditorUtility.DisplayDialog("Sol Para Todos", "No hay una escena activa. Abre Assets/Scenes/SampleScene.unity.", "OK");
            return;
        }

        AsegurarCarpetas();

        Undo.IncrementCurrentGroup();
        int grupoUndo = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Sol Para Todos: Construir escena y UI");

        _estadoLogo = "Logo: el menú no llegó a construirse.";
        var informe = new List<string>();
        try
        {
            EditorUtility.DisplayProgressBar("Sol Para Todos", "Limpiando construcción anterior…", 0.03f);
            int borrados = EliminarGenerados();
            if (borrados > 0) informe.Add("Construcción anterior reemplazada (" + borrados + " objetos SPT).");

            // ---------------------------------------------------------- Búsqueda de objetos existentes
            EditorUtility.DisplayProgressBar("Sol Para Todos", "Buscando objetos de la escena…", 0.08f);
            Light luz = BuscarLuz();
            if (luz == null)
            {
                var goLuz = new GameObject("Directional Light");
                Undo.RegisterCreatedObjectUndo(goLuz, "Crear luz");
                luz = goLuz.AddComponent<Light>();
                luz.type = LightType.Directional;
                luz.shadows = LightShadows.Soft;
                goLuz.transform.rotation = Quaternion.Euler(50f, 12f, 0f);
                informe.Add("No se encontró Directional Light: se creó una.");
            }

            Camera camPrincipal, cam2, cam3;
            BuscarCamaras(out camPrincipal, out cam2, out cam3, informe);
            Volume volGlobal = BuscarVolumenGlobal();
            if (volGlobal == null) informe.Add("Aviso: no se encontró 'Global Volume'.");

            // ---------------------------------------------------------- Granja ordenada
            EditorUtility.DisplayProgressBar("Sol Para Todos", "Ordenando la granja solar…", 0.15f);
            GeoGranja geo = ConstruirGranja(PresetActual(), informe);
            Transform[] filas = geo.valida ? geo.filas : BuscarFilas();
            informe.Add("Filas: " + filas.Length + ".");
            if (filas.Length == 0)
                informe.Add("Aviso: no hay paneles 'Fila*'. Se usarán los LOD Group de la escena como paneles.");

            // ---------------------------------------------------------- Terreno, niebla, cielo, sombras
            EditorUtility.DisplayProgressBar("Sol Para Todos", "Terreno, niebla y cielo…", 0.42f);
            float sueloY = geo.valida ? geo.sueloY : AlturaSuelo(LimitesParque(filas));
            Vector3 centroGranja = geo.valida ? geo.campo.position : LimitesParque(filas).center;
            ConfigurarTerreno(centroGranja, informe);
            Material matCielo = ConfigurarAtmosfera(informe);
            AjustarSombrasURP(informe);
            OptimizarCalidad(informe);

            // ---------------------------------------------------------- Cámaras y audio
            int listenersOff = ConfigurarCamaras(camPrincipal, cam2, cam3);
            if (listenersOff > 0) informe.Add("AudioListener duplicados desactivados: " + listenersOff + ".");

            // ---------------------------------------------------------- Celdas y material
            EditorUtility.DisplayProgressBar("Sol Para Todos", "Analizando paneles y materiales…", 0.5f);
            Material matCeldas;
            Renderer[] celdas = BuscarCeldas(filas, out matCeldas);
            informe.Add("Renderers de celdas (P. Fotovoltaico, LOD0–LOD2): " + celdas.Length + ".");
            if (matCeldas != null)
            {
                PrepararEmision(matCeldas);
                informe.Add("Material de celdas: '" + matCeldas.name + "' (compartido, _EMISSION activo en negro).");
            }

            int modulos;
            Vector3[] normales = SolarFieldController.CalcularNormales(filas, matCeldas, out modulos);
            informe.Add("Unidades Panel_LOD: " + normales.Length + " · módulos por unidad: " + modulos + ".");

            Bounds limites = LimitesParque(filas);

            // ---------------------------------------------------------- Vistas, inversores, acentos, volumen
            EditorUtility.DisplayProgressBar("Sol Para Todos", "Vistas, inversores e indicadores luminosos…", 0.58f);
            float[] distancias;
            Transform[] vistas = CrearVistas(filas, limites, sueloY, normales, matCeldas, out distancias);
            Renderer[] leds = new Renderer[0];
            try
            {
                leds = CrearInversores(geo, filas, limites, sueloY);
                informe.Add("Inversores con indicador LED: " + leds.Length + ".");
            }
            catch (Exception e) { Fallo(informe, "Inversores", e); }

            try { CrearEntorno(geo, sueloY); }
            catch (Exception e) { Fallo(informe, "Cerca y vegetación", e); }

            Renderer[] azul = new Renderer[0], verde = new Renderer[0], rojo = new Renderer[0];
            try { CrearAcentos(geo, limites, sueloY, out azul, out verde, out rojo); }
            catch (Exception e) { Fallo(informe, "Indicadores luminosos", e); }

            Volume volNoche = null;
            try { volNoche = CrearVolumenNocturno(); }
            catch (Exception e) { Fallo(informe, "Volume nocturno", e); }

            // ---------------------------------------------------------- Controlador
            var goCtrl = new GameObject("SPT_SolarField");
            Undo.RegisterCreatedObjectUndo(goCtrl, "Crear controlador");
            goCtrl.AddComponent<SPTGenerado>();
            SolarFieldController ctrl = goCtrl.AddComponent<SolarFieldController>();
            ctrl.sol = luz;
            ctrl.camaraPrincipal = camPrincipal;
            ctrl.camarasEscena = new[] { camPrincipal, cam2, cam3 };
            ctrl.volumenGlobal = volGlobal;
            ctrl.volumenNocturno = volNoche;
            ctrl.filas = filas;
            ctrl.vistas = vistas;
            ctrl.distanciaOrbita = distancias;
            ctrl.limitesRecorrido = new Bounds(limites.center, limites.size + new Vector3(12f, 0f, 12f));
            ctrl.ledsIndicadores = leds;
            ctrl.acentosAzul = azul;
            ctrl.acentosVerde = verde;
            ctrl.acentosRojo = rojo;
            ctrl.materialCielo = matCielo;
            ctrl.nieblaInicio = NieblaInicio;
            ctrl.nieblaFin = NieblaFin;
            ctrl.materialCeldas = matCeldas;
            ctrl.renderersCeldas = celdas;
            ctrl.normalesPaneles = normales;
            ctrl.modulosPorUnidad = Mathf.Max(1, modulos);
            ctrl.azimutSol = luz.transform.eulerAngles.y;
            ctrl.intensidadSolMax = Mathf.Max(0.5f, luz.intensity);
            ctrl.hora = 10f;
            ctrl.fichaTitulos = (string[])SolarFieldController.FichasTitulosPorDefecto.Clone();
            ctrl.fichaCuerpos = (string[])SolarFieldController.FichasCuerposPorDefecto.Clone();

            // ---------------------------------------------------------- UI
            EditorUtility.DisplayProgressBar("Sol Para Todos", "Sprites y fuentes de la interfaz…", 0.68f);
            try { CargarRecursosUI(informe); }
            catch (Exception e) { Fallo(informe, "Sprites/fuentes", e); }
            EditorUtility.DisplayProgressBar("Sol Para Todos", "Construyendo interfaz…", 0.82f);
            try { AsegurarEventSystem(informe); }
            catch (Exception e) { Fallo(informe, "EventSystem", e); }
            ConstruirUI(ctrl);
            informe.Add(_estadoLogo);

            EditorUtility.SetDirty(ctrl);
            EditorSceneManager.MarkSceneDirty(escena);
            Undo.CollapseUndoOperations(grupoUndo);
            Selection.activeGameObject = goCtrl;

            informe.Add("Capacidad estimada: " + ctrl.CapacidadInstaladaKWp.ToString("0") + " kWp.");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            try { File.WriteAllText(CarpetaSettings + "/SPT_UltimoInforme.txt", "ERROR FATAL:\n" + e + "\n\n" + string.Join("\n• ", informe)); } catch { }
            EditorUtility.DisplayDialog("Sol Para Todos", "Ocurrió un error durante la construcción:\n" + e.Message + "\n\nRevisa la consola. Puedes deshacer con Ctrl+Z.", "OK");
            return;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        string resumen = string.Join("\n• ", informe);
        try { File.WriteAllText(CarpetaSettings + "/SPT_UltimoInforme.txt", resumen); } catch { }
        Debug.Log("<b>[Sol Para Todos]</b> Escena construida.\n• " + resumen);
        EditorUtility.DisplayDialog("Sol Para Todos — listo",
            "Escena y UI construidas y conectadas.\n\n• " + resumen +
            "\n\nGuarda la escena (Ctrl+S) y pulsa Play.", "OK");
    }

    private static void Fallo(List<string> informe, string fase, Exception e)
    {
        Debug.LogException(e);
        informe.Add("ERROR en «" + fase + "»: " + e.GetType().Name + " — " + e.Message + " (la construcción continuó sin esta parte).");
    }

    // =================================================================== TextMesh Pro
    private static bool AsegurarTMP()
    {
        TMP_Settings ajustes = Resources.Load<TMP_Settings>("TMP Settings");
        if (ajustes != null && TMP_Settings.defaultFontAsset != null) return true;

        Type importador = BuscarTipo("TMPro.TMP_PackageResourceImporter");
        MethodInfo metodo = importador == null ? null : importador.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "ImportResources" && m.GetParameters().Length == 3);

        if (metodo != null && !_reintentoPendiente)
        {
            _reintentoPendiente = true;
            AssetDatabase.importPackageCompleted += AlImportarTMP;
            AssetDatabase.importPackageFailed += AlFallarTMP;
            AssetDatabase.importPackageCancelled += AlCancelarTMP;
            metodo.Invoke(null, new object[] { true, false, false });
            Debug.Log("[Sol Para Todos] Importando TMP Essential Resources… la construcción continuará sola al terminar.");
            return false;
        }

        EditorUtility.DisplayDialog("Sol Para Todos",
            "Faltan los recursos esenciales de TextMesh Pro.\n\nVe a Window > TextMeshPro > Import TMP Essential Resources y vuelve a ejecutar Tools > Sol Para Todos > Construir Escena Completa y UI.", "OK");
        return false;
    }

    private static void AlImportarTMP(string paquete)
    {
        QuitarCallbacksTMP();
        EditorApplication.delayCall += () => { AssetDatabase.Refresh(); EditorApplication.delayCall += Construir; };
    }

    private static void AlFallarTMP(string paquete, string error)
    {
        QuitarCallbacksTMP();
        Debug.LogError("[Sol Para Todos] No se pudo importar TMP Essential Resources: " + error);
    }

    private static void AlCancelarTMP(string paquete) { QuitarCallbacksTMP(); }

    private static void QuitarCallbacksTMP()
    {
        _reintentoPendiente = false;
        AssetDatabase.importPackageCompleted -= AlImportarTMP;
        AssetDatabase.importPackageFailed -= AlFallarTMP;
        AssetDatabase.importPackageCancelled -= AlCancelarTMP;
    }

    private static Type BuscarTipo(string nombreCompleto)
    {
        foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type t = a.GetType(nombreCompleto, false);
            if (t != null) return t;
        }
        return null;
    }

    // =================================================================== Búsqueda en escena
    private static List<T> Todos<T>() where T : Component
    {
        var lista = new List<T>();
        foreach (GameObject raiz in SceneManager.GetActiveScene().GetRootGameObjects())
            lista.AddRange(raiz.GetComponentsInChildren<T>(true));
        return lista;
    }

    private static T PorNombre<T>(string nombre) where T : Component
    {
        return Todos<T>().FirstOrDefault(c => c.gameObject.name == nombre);
    }

    private static Light BuscarLuz()
    {
        List<Light> luces = Todos<Light>().Where(l => l.type == LightType.Directional).ToList();
        return luces.FirstOrDefault(l => l.name == "Directional Light") ?? (RenderSettings.sun != null ? RenderSettings.sun : luces.FirstOrDefault());
    }

    private static void BuscarCamaras(out Camera principal, out Camera c2, out Camera c3, List<string> informe)
    {
        List<Camera> camaras = Todos<Camera>();
        principal = camaras.FirstOrDefault(c => c.name == "Main Camera")
                    ?? camaras.FirstOrDefault(c => c.CompareTag("MainCamera"))
                    ?? camaras.FirstOrDefault(c => c.name == "Camera")
                    ?? camaras.FirstOrDefault();
        c2 = camaras.FirstOrDefault(c => c.name == "Main Camera 2");
        c3 = camaras.FirstOrDefault(c => c.name == "Main Camera 3");

        if (principal == null)
        {
            var go = new GameObject("Main Camera");
            Undo.RegisterCreatedObjectUndo(go, "Crear cámara");
            principal = go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
            informe.Add("No había cámara: se creó 'Main Camera'.");
        }
        informe.Add("Cámara principal: '" + principal.name + "'" +
                    (c2 != null ? " · 'Main Camera 2'" : "") + (c3 != null ? " · 'Main Camera 3'" : "") + ".");
    }

    private static Volume BuscarVolumenGlobal()
    {
        List<Volume> vols = Todos<Volume>().Where(v => v.GetComponent<SPTGenerado>() == null).ToList();
        return vols.FirstOrDefault(v => v.name == "Global Volume") ?? vols.FirstOrDefault(v => v.isGlobal);
    }

    private static Transform[] BuscarFilas()
    {
        List<Transform> todas = Todos<Transform>();
        var filas = todas.Where(t => !(t is RectTransform) && t.name.StartsWith("Fila", StringComparison.OrdinalIgnoreCase) && !TieneAncestroFila(t)).ToList();
        if (filas.Count == 0)
        {
            filas = Todos<LODGroup>().Select(g => g.transform.parent != null ? g.transform.parent : g.transform).Distinct().ToList();
        }
        filas.Sort((a, b) => EditorUtility.NaturalCompare(a.name, b.name));
        return filas.ToArray();
    }

    private static bool TieneAncestroFila(Transform t)
    {
        for (Transform p = t.parent; p != null; p = p.parent)
            if (p.name.StartsWith("Fila", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static int EliminarGenerados()
    {
        int n = 0;
        foreach (SPTGenerado g in Todos<SPTGenerado>())
        {
            if (g == null) continue; // ya destruido junto con su padre
            Undo.DestroyObjectImmediate(g.gameObject);
            n++;
        }
        return n;
    }

    // =================================================================== Cámaras
    private static int ConfigurarCamaras(Camera principal, Camera c2, Camera c3)
    {
        if (!principal.CompareTag("MainCamera"))
        {
            Undo.RecordObject(principal.gameObject, "Etiqueta MainCamera");
            principal.gameObject.tag = "MainCamera";
        }
        if (principal.GetComponent<AudioListener>() == null) Undo.AddComponent<AudioListener>(principal.gameObject);

        // Post-procesado necesario para el Bloom del Volume nocturno; fondo = Skybox (cielo SPT).
        foreach (Camera c in new[] { principal, c2, c3 })
        {
            if (c == null) continue;
            UniversalAdditionalCameraData datos = c.GetComponent<UniversalAdditionalCameraData>();
            if (datos == null) datos = Undo.AddComponent<UniversalAdditionalCameraData>(c.gameObject);
            Undo.RecordObject(datos, "Post-procesado");
            datos.renderPostProcessing = true;
            if (c.clearFlags != CameraClearFlags.Skybox)
            {
                Undo.RecordObject(c, "Cielo");
                c.clearFlags = CameraClearFlags.Skybox; // el cielo en degradado se dibuja como skybox
            }
            if (c.farClipPlane < 1500f)
            {
                Undo.RecordObject(c, "Far clip");
                c.farClipPlane = 1500f;
            }
        }

        // Un solo AudioListener activo: elimina el aviso "There are 2 audio listeners in the scene".
        int desactivados = 0;
        foreach (AudioListener al in Todos<AudioListener>())
        {
            if (al.gameObject == principal.gameObject || !al.enabled) continue;
            Undo.RecordObject(al, "Desactivar AudioListener");
            al.enabled = false;
            desactivados++;
        }
        return desactivados;
    }

    // =================================================================== Celdas y materiales
    private static Renderer[] BuscarCeldas(Transform[] filas, out Material material)
    {
        var porNombre = new List<Renderer>();
        foreach (Transform f in filas)
            foreach (Renderer r in f.GetComponentsInChildren<Renderer>(true))
                if (SolarFieldController.EsCelda(r, null)) porNombre.Add(r);

        material = porNombre.Where(r => r.sharedMaterial != null)
                            .GroupBy(r => r.sharedMaterial)
                            .OrderByDescending(g => g.Count())
                            .Select(g => g.Key).FirstOrDefault();

        if (material == null) return porNombre.ToArray();

        var resultado = new List<Renderer>();
        foreach (Transform f in filas)
            foreach (Renderer r in f.GetComponentsInChildren<Renderer>(true))
                if (SolarFieldController.EsCelda(r, material)) resultado.Add(r);
        return resultado.ToArray();
    }

    private static void PrepararEmision(Material m)
    {
        if (!m.HasProperty("_EmissionColor")) return;
        Undo.RecordObject(m, "Activar emisión de celdas");
        // Emisión prácticamente negra: el aspecto no cambia, pero la variante _EMISSION queda incluida
        // en el build y el MaterialPropertyBlock puede encender el brillo en runtime.
        Color actual = m.GetColor("_EmissionColor");
        if (actual.maxColorComponent < 0.001f) m.SetColor("_EmissionColor", new Color(0.0015f, 0.0015f, 0.0015f, 1f));
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        m.EnableKeyword("_EMISSION");
        EditorUtility.SetDirty(m);
        AssetDatabase.SaveAssetIfDirty(m);
    }

    private static Material CrearMaterial(string nombre, Color colorBase, Color emision, float suavidad, float metalico)
    {
        string ruta = CarpetaMateriales + "/" + nombre + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(ruta);
        bool nuevo = m == null;
        if (nuevo)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            m = new Material(sh);
        }
        m.SetColor("_BaseColor", colorBase);
        m.SetFloat("_Smoothness", suavidad);
        m.SetFloat("_Metallic", metalico);
        if (emision.maxColorComponent > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emision);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        m.enableInstancing = false; // SRP Batcher tiene prioridad sobre GPU Instancing en URP
        if (nuevo) AssetDatabase.CreateAsset(m, ruta);
        else EditorUtility.SetDirty(m);
        return m;
    }

    // =================================================================== Geometría del parque
    private static Bounds LimitesParque(Transform[] filas)
    {
        bool hay = false;
        var b = new Bounds();
        foreach (Transform f in filas)
            foreach (Renderer r in f.GetComponentsInChildren<Renderer>(false))
            {
                if (!hay) { b = r.bounds; hay = true; }
                else b.Encapsulate(r.bounds);
            }
        if (!hay) b = new Bounds(Vector3.zero, new Vector3(60f, 4f, 60f));
        return b;
    }

    private static Bounds LimitesDe(Transform t)
    {
        bool hay = false;
        var b = new Bounds(t.position, Vector3.zero);
        foreach (Renderer r in t.GetComponentsInChildren<Renderer>(false))
        {
            if (!hay) { b = r.bounds; hay = true; }
            else b.Encapsulate(r.bounds);
        }
        return b;
    }

    private static float AlturaSuelo(Bounds parque)
    {
        Renderer pasto = Todos<Renderer>().FirstOrDefault(r => r.name == "Pasto");
        if (pasto != null)
        {
            float y = pasto.bounds.max.y;
            if (y <= parque.center.y) return y;
        }
        return parque.min.y;
    }

    private static Transform[] CrearVistas(Transform[] filas, Bounds parque, float sueloY, Vector3[] normales, Material matCeldas, out float[] distancias)
    {
        var raiz = new GameObject("SPT_Vistas");
        Undo.RegisterCreatedObjectUndo(raiz, "Crear vistas");
        raiz.AddComponent<SPTGenerado>();

        Vector3 normalMedia = Vector3.zero;
        foreach (Vector3 n in normales) normalMedia += n;
        Vector3 frente = new Vector3(normalMedia.x, 0f, normalMedia.z);
        if (frente.sqrMagnitude < 1e-4f) frente = Vector3.back;
        frente.Normalize();
        Vector3 lateral = Vector3.Cross(Vector3.up, frente).normalized;

        Vector3 c = parque.center;
        float radio = Mathf.Max(10f, new Vector2(parque.extents.x, parque.extents.z).magnitude);

        // Vista 1 — Panorámica aérea desde el lado hacia el que miran los paneles.
        Vector3 p1 = c + frente * (radio * 0.95f) + lateral * (radio * 0.25f) + Vector3.up * (radio * 0.6f);
        Quaternion r1 = Quaternion.LookRotation(c - p1, Vector3.up);
        float d1 = Vector3.Distance(p1, c);

        // Vista 2 — Detalle de una unidad del frente central (LOD0).
        LODGroup elegido = null;
        float mejor = float.MinValue;
        foreach (Transform f in filas)
            foreach (LODGroup g in f.GetComponentsInChildren<LODGroup>(false))
            {
                Vector3 off = LimitesDe(g.transform).center - c;
                float puntaje = Vector3.Dot(off, frente) - 0.6f * Mathf.Abs(Vector3.Dot(off, lateral));
                if (puntaje > mejor) { mejor = puntaje; elegido = g; }
            }
        Vector3 p2; Quaternion r2; float d2;
        if (elegido != null)
        {
            Bounds ub = LimitesDe(elegido.transform);
            Vector3 n = Vector3.up;
            LOD[] lods = elegido.GetLODs();
            if (lods.Length > 0)
            {
                Vector3 suma = Vector3.zero;
                foreach (Renderer r in lods[0].renderers)
                    if (r != null && SolarFieldController.EsCelda(r, matCeldas)) suma += SolarFieldController.NormalDesdeRenderer(r);
                if (suma.sqrMagnitude > 0f) n = suma.normalized;
            }
            float tam = Mathf.Max(1.5f, ub.extents.magnitude);
            Vector3 dir = (n * 1.0f + frente * 0.5f + lateral * 0.45f).normalized;
            p2 = ub.center + dir * (tam * 1.15f);
            r2 = Quaternion.LookRotation(ub.center - p2, Vector3.up);
            d2 = Vector3.Distance(p2, ub.center);
        }
        else
        {
            p2 = c + frente * 8f + Vector3.up * 3f;
            r2 = Quaternion.LookRotation(c - p2);
            d2 = 8f;
        }

        // Vista 3 — A nivel de suelo, en el pasillo entre dos filas paralelas.
        Vector3 p3; Quaternion r3;
        CalcularPasillo(filas, parque, sueloY, out p3, out r3);

        distancias = new[] { d1, d2, 0f };
        return new[]
        {
            CrearAncla(raiz.transform, "SPT_Vista_1_Panoramica", p1, r1),
            CrearAncla(raiz.transform, "SPT_Vista_2_DetallePanel", p2, r2),
            CrearAncla(raiz.transform, "SPT_Vista_3_Estructura", p3, r3)
        };
    }

    private static void CalcularPasillo(Transform[] filas, Bounds parque, float sueloY, out Vector3 pos, out Quaternion rot)
    {
        float altura = sueloY + 1.6f;
        var bs = filas.Select(LimitesDe).Where(b => b.size.sqrMagnitude > 0.01f).ToList();
        if (bs.Count == 0)
        {
            pos = new Vector3(parque.min.x - 2f, altura, parque.center.z);
            rot = Quaternion.LookRotation(Vector3.right);
            return;
        }

        Bounds refB = bs.OrderBy(b => (b.center - parque.center).sqrMagnitude).First();
        bool largoX = refB.extents.x >= refB.extents.z;
        Func<Vector3, float> L = v => largoX ? v.x : v.z;   // eje largo de la fila
        Func<Vector3, float> S = v => largoX ? v.z : v.x;   // eje corto (entre filas)

        Bounds? vecina = null;
        float mejor = float.MaxValue;
        foreach (Bounds b in bs)
        {
            float sep = Mathf.Abs(S(b.center) - S(refB.center));
            if (sep < 0.5f) continue;
            float solape = Mathf.Min(L(b.max), L(refB.max)) - Mathf.Max(L(b.min), L(refB.min));
            if (solape < 0.4f * (L(refB.max) - L(refB.min))) continue;
            if (sep < mejor) { mejor = sep; vecina = b; }
        }

        float sMedio;
        if (vecina.HasValue)
        {
            Bounds v = vecina.Value;
            float bordeA = S(refB.center) < S(v.center) ? S(refB.max) : S(refB.min);
            float bordeB = S(refB.center) < S(v.center) ? S(v.min) : S(v.max);
            sMedio = (bordeA + bordeB) * 0.5f;
        }
        else
        {
            sMedio = S(refB.max) + 1.5f;
        }

        float lInicio = L(refB.min) - 1.5f;
        Vector3 adelante = largoX ? Vector3.right : Vector3.forward;
        pos = largoX ? new Vector3(lInicio, altura, sMedio) : new Vector3(sMedio, altura, lInicio);
        rot = Quaternion.LookRotation(adelante, Vector3.up) * Quaternion.Euler(-4f, 0f, 0f);
    }

    private static Transform CrearAncla(Transform padre, string nombre, Vector3 p, Quaternion r)
    {
        var go = new GameObject(nombre);
        Undo.RegisterCreatedObjectUndo(go, "Crear ancla");
        go.transform.SetParent(padre, false);
        go.transform.SetPositionAndRotation(p, r);
        return go.transform;
    }

    // =================================================================== GRANJA SOLAR (rejilla simétrica)
    private static PresetGranja PresetActual()
    {
        int i = Mathf.Clamp(EditorPrefs.GetInt(ClavePreset, 1), 0, Presets.Length - 1);
        return Presets[i];
    }

    /// <summary>Centros de una mitad de la rejilla, espejados respecto al origen. Longitud = 2·mitad, ascendente.</summary>
    public static float[] CalcularRejilla(int mitad, float huella, float separacion, float calle)
    {
        var r = new float[mitad * 2];
        float paso = huella + separacion;
        for (int k = 0; k < mitad; k++)
        {
            float v = calle * 0.5f + huella * 0.5f + k * paso;
            r[mitad + k] = v;
            r[mitad - 1 - k] = -v;
        }
        return r;
    }

    private static Transform RaizUnidad(LODGroup g)
    {
        GameObject raiz = PrefabUtility.GetOutermostPrefabInstanceRoot(g.gameObject);
        return raiz != null ? raiz.transform : g.transform;
    }

    /// <summary>Huella de una unidad (LOD0) medida en el espacio del campo: tamaño X/Z y centro.</summary>
    private static void MedirHuella(Transform unidad, Transform campo, out Vector3 centro, out Vector2 tam)
    {
        LODGroup g = unidad.GetComponentInChildren<LODGroup>(true);
        Renderer[] rs = null;
        if (g != null && g.lodCount > 0) rs = g.GetLODs()[0].renderers;
        if (rs == null || rs.Length == 0) rs = unidad.GetComponentsInChildren<Renderer>(true);

        bool hay = false;
        Vector3 min = Vector3.zero, max = Vector3.zero;
        foreach (Renderer r in rs)
        {
            if (r == null) continue;
            Bounds lb = r.localBounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                Vector3 p = campo.InverseTransformPoint(r.transform.TransformPoint(c));
                if (!hay) { min = max = p; hay = true; }
                else { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            }
        }
        if (!hay)
        {
            centro = campo.InverseTransformPoint(unidad.position);
            tam = new Vector2(5f, 9.5f);
            return;
        }
        centro = (min + max) * 0.5f;
        tam = new Vector2(Mathf.Max(0.5f, max.x - min.x), Mathf.Max(0.5f, max.z - min.z));
    }

    /// <summary>Altura de la superficie del «Pasto» (o el valor de respaldo si no existe).</summary>
    private static float SueloY(float respaldo)
    {
        Renderer pasto = Todos<Renderer>().FirstOrDefault(r => r.name == "Pasto");
        return pasto != null ? pasto.bounds.max.y : respaldo;
    }

    private static GeoGranja ConstruirGranja(PresetGranja preset, List<string> informe)
    {
        var geo = new GeoGranja();

        // ---- 1) Unidades existentes (cada instancia de prefab con LODGroup = una unidad de panel)
        Transform[] viejas = BuscarFilas();
        var pool = new List<Transform>();
        foreach (Transform f in viejas)
            foreach (LODGroup g in f.GetComponentsInChildren<LODGroup>(true))
            {
                Transform r = RaizUnidad(g);
                if (!pool.Contains(r)) pool.Add(r);
            }
        if (pool.Count == 0)
        {
            informe.Add("Granja: no se encontraron unidades de panel (LODGroup); se omite el reordenamiento.");
            return geo;
        }

        // ---- 2) Campo (contenedor) y marco de referencia
        Transform campo = null;
        foreach (Transform f in viejas)
            if (f.parent != null && f.parent.name == "Campo") { campo = f.parent; break; }
        if (campo == null)
        {
            GameObject goCampo = GameObject.Find("Campo");
            if (goCampo != null) campo = goCampo.transform;
        }
        Bounds viejoLimite = LimitesParque(viejas);
        if (campo == null)
        {
            var go = new GameObject("Campo");
            Undo.RegisterCreatedObjectUndo(go, "Crear Campo");
            go.transform.position = new Vector3(viejoLimite.center.x, 0f, viejoLimite.center.z);
            campo = go.transform;
            informe.Add("Granja: se creó el contenedor 'Campo'.");
        }
        geo.campo = campo;

        // Orden estable: por posición actual (X, luego Z).
        pool = pool.OrderBy(t => Mathf.Round(t.position.x * 2f)).ThenBy(t => t.position.z).ToList();
        Transform plantilla = pool[pool.Count / 2];

        Vector3 centroLocal;
        Vector2 huella;
        MedirHuella(plantilla, campo, out centroLocal, out huella);
        Vector3 posPlantillaLocal = campo.InverseTransformPoint(plantilla.position);
        Vector3 desfase = centroLocal - posPlantillaLocal;           // pivote → centro de la huella
        Quaternion rotLocal = Quaternion.Inverse(campo.rotation) * plantilla.rotation;
        float yLocal = posPlantillaLocal.y;
        Vector3 escalaLocal = plantilla.localScale;

        geo.huella = huella;
        geo.xs = CalcularRejilla(preset.filasPorBloque, huella.x, GapPasillo, CarreteraX);
        geo.zs = CalcularRejilla(preset.unidadesPorBloque, huella.y, GapUnidad, CarreteraZ);
        geo.anchoX = geo.xs[geo.xs.Length - 1] - geo.xs[0] + huella.x;
        geo.largoZ = geo.zs[geo.zs.Length - 1] - geo.zs[0] + huella.y;
        int total = geo.xs.Length * geo.zs.Length;
        geo.unidades = total;

        // ---- 3) Sacar las unidades de las filas viejas y limpiar las filas vacías
        // Se clona la plantilla ANTES de mover nada (conserva los overrides de material de la instancia).
        int reutilizadas = Mathf.Min(pool.Count, total);
        int creadas = Mathf.Max(0, total - pool.Count);
        int quitadas = Mathf.Max(0, pool.Count - total);

        EditorUtility.DisplayProgressBar("Sol Para Todos", "Granja: liberando unidades antiguas…", 0.18f);
        foreach (Transform u in pool)
            if (u.parent != campo) Undo.SetTransformParent(u, campo, "Mover panel");

        foreach (Transform f in viejas)
        {
            if (f == null || f == campo) continue;
            if (f.childCount == 0) Undo.DestroyObjectImmediate(f.gameObject);
            else informe.Add("Aviso: '" + f.name + "' conserva " + f.childCount + " hijos que no son paneles; no se eliminó.");
        }

        // ---- 4) Rejilla: filas «Fila NN» (oeste → este) y unidades (sur → norte)
        var filas = new Transform[geo.xs.Length];
        int cursor = 0;
        for (int i = 0; i < geo.xs.Length; i++)
        {
            if (i % 4 == 0)
                EditorUtility.DisplayProgressBar("Sol Para Todos", "Granja: fila " + (i + 1) + " de " + geo.xs.Length + "…", 0.2f + 0.2f * i / geo.xs.Length);

            var goFila = new GameObject("Fila " + (i + 1).ToString("00"));
            Undo.RegisterCreatedObjectUndo(goFila, "Crear fila");
            Transform tf = goFila.transform;
            tf.SetParent(campo, false);
            tf.localPosition = new Vector3(geo.xs[i], 0f, 0f);
            tf.localRotation = Quaternion.identity;
            tf.localScale = Vector3.one;
            filas[i] = tf;

            for (int k = 0; k < geo.zs.Length; k++)
            {
                Transform u;
                if (cursor < pool.Count) u = pool[cursor];
                else
                {
                    GameObject clon = (GameObject)Object.Instantiate(plantilla.gameObject);
                    Undo.RegisterCreatedObjectUndo(clon, "Clonar panel");
                    u = clon.transform;
                }
                cursor++;

                Undo.SetTransformParent(u, tf, "Colocar panel");
                Undo.RecordObject(u, "Colocar panel");
                u.gameObject.SetActive(true);
                u.localPosition = new Vector3(-desfase.x, yLocal, geo.zs[k] - desfase.z);
                u.localRotation = rotLocal;
                u.localScale = escalaLocal;
                u.name = "Panel " + (i + 1).ToString("00") + "-" + (k + 1).ToString("00");
            }
        }

        // ---- 5) Sobrantes
        for (int i = total; i < pool.Count; i++)
            if (pool[i] != null) Undo.DestroyObjectImmediate(pool[i].gameObject);

        // ---- 6) Centrar la granja donde estaba el parque original
        Undo.RecordObject(campo, "Centrar granja");
        campo.position = new Vector3(viejoLimite.center.x, campo.position.y, viejoLimite.center.z);

        geo.filas = filas;
        geo.sueloY = SueloY(campo.position.y);
        geo.valida = true;
        informe.Add("Granja ordenada: " + geo.xs.Length + " filas × " + geo.zs.Length + " unidades = " + total +
                    " (reutilizadas " + reutilizadas + ", clonadas " + creadas + ", eliminadas " + quitadas + ") · " +
                    geo.anchoX.ToString("0") + " × " + geo.largoZ.ToString("0") + " m · huella de unidad " +
                    huella.x.ToString("0.0") + " × " + huella.y.ToString("0.0") + " m.");
        return geo;
    }

    // =================================================================== TERRENO + NIEBLA + CIELO
    private static void ConfigurarTerreno(Vector3 centro, List<string> informe)
    {
        Renderer pasto = Todos<Renderer>().FirstOrDefault(r => r.name == "Pasto");
        if (pasto == null)
        {
            informe.Add("Aviso: no se encontró el objeto 'Pasto'; el terreno infinito no se creó.");
            return;
        }

        // Material propio (el original no se modifica) con mosaico fino y niebla.
        Material origen = pasto.sharedMaterial;
        Texture textura = origen != null ? (origen.HasProperty("_BaseMap") ? origen.GetTexture("_BaseMap") : origen.mainTexture) : null;
        Color tinte = origen != null && origen.HasProperty("_BaseColor") ? origen.GetColor("_BaseColor") : Color.white;
        string rutaMat = CarpetaMateriales + "/SPT_Pasto.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(rutaMat);
        bool nuevo = mat == null;
        if (nuevo)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            mat = new Material(sh != null ? sh : Shader.Find("Standard"));
        }
        if (textura != null) mat.SetTexture("_BaseMap", textura);
        mat.SetTextureScale("_BaseMap", Vector2.one);
        // Variación a gran escala (claros, oscuros y manchas secas) para que el césped no se vea uniforme.
        Texture2D variacion = TexturaVariacionPasto();
        if (variacion != null)
        {
            mat.SetTexture("_DetailAlbedoMap", variacion);
            float rep = TamMosaicoPasto / 70f;   // una repetición del mapa cada 70 m
            mat.SetTextureScale("_DetailAlbedoMap", new Vector2(rep, rep));
            mat.SetFloat("_DetailAlbedoMapScale", 1f);
            mat.EnableKeyword("_DETAIL_MULX2");
        }
        mat.SetColor("_BaseColor", tinte);
        mat.SetFloat("_Smoothness", 0.04f);
        mat.SetFloat("_Metallic", 0f);
        mat.enableInstancing = false;
        if (nuevo) AssetDatabase.CreateAsset(mat, rutaMat); else EditorUtility.SetDirty(mat);

        // La textura se ve de lado a muy poca altura: mipmaps + anisotropía (solo si hace falta).
        if (textura != null)
        {
            string rutaTex = AssetDatabase.GetAssetPath(textura);
            TextureImporter ti = string.IsNullOrEmpty(rutaTex) ? null : AssetImporter.GetAtPath(rutaTex) as TextureImporter;
            if (ti != null && (ti.anisoLevel < 4 || !ti.mipmapEnabled || ti.wrapMode != TextureWrapMode.Repeat))
            {
                ti.anisoLevel = 4;
                ti.mipmapEnabled = true;
                ti.wrapMode = TextureWrapMode.Repeat;
                ti.SaveAndReimport();
                informe.Add("Textura de pasto: mipmaps + anisotropía 4 (para ver el mosaico lejano sin parpadeo).");
            }
        }

        // Malla de disco con anillos cada vez más espaciados (la niebla se calcula por vértice en URP).
        Mesh malla = CrearMallaTerreno(RadioTerreno, TamMosaicoPasto);
        string rutaMalla = CarpetaMallas + "/SPT_Terreno.asset";
        AssetDatabase.DeleteAsset(rutaMalla);
        AssetDatabase.CreateAsset(malla, rutaMalla);

        Transform t = pasto.transform;
        Undo.RecordObject(t, "Terreno");
        t.rotation = Quaternion.identity;
        t.localScale = Vector3.one;
        t.position = new Vector3(centro.x, t.position.y, centro.z);

        MeshFilter mf = pasto.GetComponent<MeshFilter>();
        if (mf != null) { Undo.RecordObject(mf, "Terreno"); mf.sharedMesh = malla; }
        Undo.RecordObject(pasto, "Terreno");
        pasto.sharedMaterial = mat;
        pasto.shadowCastingMode = ShadowCastingMode.Off;
        pasto.receiveShadows = true;
        MeshCollider mc = pasto.GetComponent<MeshCollider>();
        if (mc != null) { Undo.RecordObject(mc, "Terreno"); mc.sharedMesh = malla; }
        EditorUtility.SetDirty(pasto);

        informe.Add("Terreno: disco de " + (RadioTerreno * 2f).ToString("0") + " m con mosaico de " + TamMosaicoPasto.ToString("0") + " m (material SPT_Pasto).");
    }

    private static Mesh CrearMallaTerreno(float radio, float mosaico)
    {
        const int segmentos = 72;
        var radios = new List<float>();
        float r = 6f;
        while (r < radio) { radios.Add(r); r *= 1.22f; }
        radios.Add(radio);

        var v = new List<Vector3>();
        var uv = new List<Vector2>();
        v.Add(Vector3.zero); uv.Add(Vector2.zero);
        foreach (float rr in radios)
            for (int i = 0; i < segmentos; i++)
            {
                float a = 2f * Mathf.PI * i / segmentos;
                Vector3 p = new Vector3(Mathf.Sin(a) * rr, 0f, Mathf.Cos(a) * rr);
                v.Add(p);
                uv.Add(new Vector2(p.x / mosaico, p.z / mosaico));
            }

        var tri = new List<int>();
        for (int i = 0; i < segmentos; i++)                       // abanico central (normal hacia arriba)
        {
            tri.Add(0); tri.Add(1 + i); tri.Add(1 + (i + 1) % segmentos);
        }
        for (int k = 0; k < radios.Count - 1; k++)                // coronas
        {
            int a0 = 1 + k * segmentos, b0 = 1 + (k + 1) * segmentos;
            for (int i = 0; i < segmentos; i++)
            {
                int n = (i + 1) % segmentos;
                int a = a0 + i, b = a0 + n, c = b0 + i, d = b0 + n;
                tri.Add(a); tri.Add(c); tri.Add(b);
                tri.Add(b); tri.Add(c); tri.Add(d);
            }
        }

        var m = new Mesh { name = "SPT_Terreno", indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
        m.SetVertices(v);
        m.SetUVs(0, uv);
        m.SetTriangles(tri, 0);
        var normales = new Vector3[v.Count];
        for (int i = 0; i < normales.Length; i++) normales[i] = Vector3.up;
        m.normals = normales;
        m.bounds = new Bounds(Vector3.zero, new Vector3(radio * 2f, 4f, radio * 2f));
        return m;
    }

    private static Material ConfigurarAtmosfera(List<string> informe)
    {
        // Niebla lineal PERSISTENTE: así las variantes de fog no se eliminan del build de Android.
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = NieblaInicio;
        RenderSettings.fogEndDistance = NieblaFin;
        RenderSettings.fogColor = new Color(0.66f, 0.80f, 0.94f, 1f);

        // Ambiente Trilight (el controlador lo anima con la hora y el modo noche).
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.46f, 0.58f, 0.80f, 1f);
        RenderSettings.ambientEquatorColor = new Color(0.52f, 0.60f, 0.68f, 1f);
        RenderSettings.ambientGroundColor = new Color(0.30f, 0.28f, 0.24f, 1f);
        RenderSettings.ambientIntensity = 1f;

        Shader sh = Shader.Find("SolParaTodos/SkyGradient");
        if (sh == null)
        {
            informe.Add("Aviso: no se encontró el shader SolParaTodos/SkyGradient (Assets/SolParaTodos/Shaders). Se conserva tu skybox; la niebla sí se aplicó.");
            return null;
        }
        string ruta = CarpetaMateriales + "/SPT_Cielo.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(ruta);
        bool nuevo = m == null;
        if (nuevo) m = new Material(sh);
        m.shader = sh;
        m.SetColor("_Zenith", new Color(0.13f, 0.34f, 0.74f).linear);
        m.SetColor("_Horizon", new Color(0.66f, 0.80f, 0.94f).linear);
        m.SetColor("_GroundCol", new Color(0.43f, 0.52f, 0.61f).linear);
        if (nuevo) AssetDatabase.CreateAsset(m, ruta); else EditorUtility.SetDirty(m);

        RenderSettings.skybox = m;
        DynamicGI.UpdateEnvironment(); // refresca ambiente / reflejos con el nuevo cielo
        informe.Add("Cielo en degradado (SPT_Cielo) y niebla lineal " + NieblaInicio.ToString("0") + "–" + NieblaFin.ToString("0") + " m con el color del horizonte.");
        return m;
    }

    private static void AjustarSombrasURP(List<string> informe)
    {
        RenderPipelineAsset rpa = QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline;
        UniversalRenderPipelineAsset urp = rpa as UniversalRenderPipelineAsset;
        if (urp == null) return;

        // Con la granja más grande, 50 m de sombras dejan sin sombra la vista panorámica.
        const float objetivo = 130f;
        if (urp.shadowDistance >= objetivo - 0.5f) return;
        if (!File.Exists(RutaRespaldoURP))
        {
            File.WriteAllText(RutaRespaldoURP, urp.name + "|" + urp.shadowDistance.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AssetDatabase.ImportAsset(RutaRespaldoURP);
        }
        float anterior = urp.shadowDistance;
        urp.shadowDistance = objetivo;
        EditorUtility.SetDirty(urp);
        AssetDatabase.SaveAssetIfDirty(urp);
        informe.Add("URP '" + urp.name + "': distancia de sombras " + anterior.ToString("0") + " → " + objetivo.ToString("0") +
                    " m (respaldo en SPT_URP_Respaldo.txt; menú «Restaurar sombras URP»).");
    }

    /// <summary>
    /// Los niveles de calidad con LOD Bias &gt; 1 mantienen los paneles en LOD0/LOD1 (muchos más triángulos y draw calls)
    /// aunque estén lejos. Con bias 1 los LOD de tu modelo trabajan como fueron diseñados.
    /// </summary>
    private static void OptimizarCalidad(List<string> informe)
    {
        int actual = QualitySettings.GetQualityLevel();
        int cambiados = 0;
        string[] nombres = QualitySettings.names;
        for (int i = 0; i < nombres.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            if (QualitySettings.lodBias > 1f)
            {
                QualitySettings.lodBias = 1f;
                cambiados++;
            }
        }
        QualitySettings.SetQualityLevel(actual, false);
        informe.Add(cambiados > 0
            ? "Rendimiento: LOD Bias reducido a 1 en " + cambiados + " nivel(es) de calidad (menos triángulos y draw calls)."
            : "Rendimiento: LOD Bias ya estaba en 1.");
    }

    // =================================================================== Mallas combinadas (indicadores)
    private static void AgregarEsfera(List<Vector3> v, List<int> t, Vector3 c, float r)
    {
        const int seg = 8, ani = 4;
        int baseIdx = v.Count;
        for (int j = 0; j <= ani; j++)
        {
            float phi = Mathf.PI * j / ani;
            for (int i = 0; i <= seg; i++)
            {
                float th = 2f * Mathf.PI * i / seg;
                v.Add(c + new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th)) * r);
            }
        }
        for (int j = 0; j < ani; j++)
            for (int i = 0; i < seg; i++)
            {
                int a = baseIdx + j * (seg + 1) + i, b = a + 1, c2 = a + seg + 1, d = c2 + 1;
                t.Add(a); t.Add(b); t.Add(c2);
                t.Add(b); t.Add(d); t.Add(c2);
            }
    }

    private static void AgregarCilindro(List<Vector3> v, List<int> t, Vector3 baseCentro, float r, float alto)
    {
        const int seg = 8;
        int b0 = v.Count;
        for (int i = 0; i < seg; i++)
        {
            float th = 2f * Mathf.PI * i / seg;
            Vector3 d = new Vector3(Mathf.Cos(th), 0f, Mathf.Sin(th)) * r;
            v.Add(baseCentro + d);
            v.Add(baseCentro + d + Vector3.up * alto);
        }
        for (int i = 0; i < seg; i++)
        {
            int a = b0 + i * 2, b = b0 + ((i + 1) % seg) * 2;
            t.Add(a); t.Add(a + 1); t.Add(b);
            t.Add(b); t.Add(a + 1); t.Add(b + 1);
        }
    }

    private static Renderer CrearObjetoMalla(Transform padre, string nombre, List<Vector3> v, List<int> t, Material mat, bool sombras, List<Vector2> uv = null)
    {
        var m = new Mesh { name = nombre, indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
        m.SetVertices(v);
        if (uv != null) m.SetUVs(0, uv);
        m.SetTriangles(t, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        string ruta = CarpetaMallas + "/" + nombre + ".asset";
        AssetDatabase.DeleteAsset(ruta);
        AssetDatabase.CreateAsset(m, ruta);

        var go = new GameObject(nombre);
        Undo.RegisterCreatedObjectUndo(go, nombre);
        go.transform.SetParent(padre, false);
        go.AddComponent<MeshFilter>().sharedMesh = m;
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = sombras ? ShadowCastingMode.On : ShadowCastingMode.Off;
        mr.receiveShadows = sombras;
        return mr;
    }

    // =================================================================== Indicadores emisivos de la granja
    private static void CrearAcentos(GeoGranja geo, Bounds parque, float sueloY, out Renderer[] azul, out Renderer[] verde, out Renderer[] rojo)
    {
        azul = new Renderer[0]; verde = new Renderer[0]; rojo = new Renderer[0];

        Material mAzul = CrearMaterial("SPT_Acento_Azul", new Color(0.02f, 0.06f, 0.12f), new Color(0.10f, 0.55f, 1.00f) * 0.6f, 0.2f, 0f);
        Material mVerde = CrearMaterial("SPT_Acento_Verde", new Color(0.02f, 0.10f, 0.04f), new Color(0.10f, 1.00f, 0.40f) * 0.6f, 0.2f, 0f);
        Material mRojo = CrearMaterial("SPT_Acento_Rojo", new Color(0.12f, 0.02f, 0.02f), new Color(1.00f, 0.12f, 0.10f) * 0.6f, 0.2f, 0f);
        Material mPoste = CrearMaterial("SPT_Poste", new Color(0.16f, 0.17f, 0.19f), Color.black, 0.35f, 0.5f);

        var raiz = new GameObject("SPT_Indicadores");
        Undo.RegisterCreatedObjectUndo(raiz, "Indicadores");
        raiz.AddComponent<SPTGenerado>();

        var vA = new List<Vector3>(); var tA = new List<int>();
        var vV = new List<Vector3>(); var tV = new List<int>();
        var vR = new List<Vector3>(); var tR = new List<int>();
        var vP = new List<Vector3>(); var tP = new List<int>();

        Vector3 c = parque.center;
        float hx, hz;
        Func<float, float, Vector3> aMundo;
        if (geo != null && geo.valida)
        {
            hx = geo.anchoX * 0.5f; hz = geo.largoZ * 0.5f;
            Transform campo = geo.campo;
            aMundo = (x, z) => { Vector3 w = campo.TransformPoint(new Vector3(x, 0f, z)); w.y = sueloY; return w; };
        }
        else
        {
            hx = parque.extents.x; hz = parque.extents.z;
            aMundo = (x, z) => new Vector3(c.x + x, sueloY, c.z + z);
        }

        // (1) Balizas azules a ras de suelo en los dos extremos de cada fila: dibujan la rejilla de noche.
        if (geo != null && geo.valida)
            foreach (float x in geo.xs)
                foreach (float s in new[] { -1f, 1f })
                    AgregarEsfera(vA, tA, aMundo(x, s * (hz + 1.4f)) + Vector3.up * 0.22f, 0.17f);

        // (2) Perímetro: camino de luces azules con postes + balizas rojas en las 4 esquinas.
        float px = hx + MargenPerimetro - 3.8f, pz = hz + MargenPerimetro - 3.8f; // farolas en el borde interior del camino
        float paso = 22f;
        int nx = Mathf.Max(2, Mathf.CeilToInt(2f * px / paso));
        int nz = Mathf.Max(2, Mathf.CeilToInt(2f * pz / paso));
        for (int i = 0; i <= nx; i++)
        {
            float x = Mathf.Lerp(-px, px, (float)i / nx);
            foreach (float s in new[] { -1f, 1f })
            {
                bool esquina = i == 0 || i == nx;
                Vector3 b = aMundo(x, s * pz);
                AgregarCilindro(vP, tP, b, esquina ? 0.10f : 0.06f, esquina ? 3.0f : 2.0f);
                if (esquina) AgregarEsfera(vR, tR, b + Vector3.up * 3.15f, 0.26f);
                else AgregarEsfera(vA, tA, b + Vector3.up * 2.1f, 0.15f);
            }
        }
        for (int j = 1; j < nz; j++)
        {
            float z = Mathf.Lerp(-pz, pz, (float)j / nz);
            foreach (float s in new[] { -1f, 1f })
            {
                Vector3 b = aMundo(s * px, z);
                AgregarCilindro(vP, tP, b, 0.06f, 2.0f);
                AgregarEsfera(vA, tA, b + Vector3.up * 2.1f, 0.15f);
            }
        }

        // (3) Balizas verdes bajas a lo largo de las dos calles centrales («pista» de los inversores).
        for (float z = -hz; z <= hz + 0.1f; z += 14f)
            foreach (float s in new[] { -1f, 1f })
                AgregarEsfera(vV, tV, aMundo(s * 3.9f, z) + Vector3.up * 0.2f, 0.14f);

        Renderer rA = CrearObjetoMalla(raiz.transform, "SPT_Acentos_Azul", vA, tA, mAzul, false);
        Renderer rV = CrearObjetoMalla(raiz.transform, "SPT_Acentos_Verde", vV, tV, mVerde, false);
        Renderer rR = CrearObjetoMalla(raiz.transform, "SPT_Acentos_Rojo", vR, tR, mRojo, false);
        CrearObjetoMalla(raiz.transform, "SPT_Postes", vP, tP, mPoste, true);

        azul = new[] { rA }; verde = new[] { rV }; rojo = new[] { rR };
    }

    // =================================================================== Texturas procedurales (grava, tierra, variación del pasto)
    private static float Hash2(int x, int y, int semilla)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + semilla * 1442695041;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    private static float RuidoTeselable(float u, float v, int freq, int semilla)
    {
        float x = u * freq, y = v * freq;
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
        x0 = ((x0 % freq) + freq) % freq; y0 = ((y0 % freq) + freq) % freq;
        int x1 = (x0 + 1) % freq, y1 = (y0 + 1) % freq;
        float a = Mathf.Lerp(Hash2(x0, y0, semilla), Hash2(x1, y0, semilla), fx);
        float b = Mathf.Lerp(Hash2(x0, y1, semilla), Hash2(x1, y1, semilla), fx);
        return Mathf.Lerp(a, b, fy);
    }

    private static float Fbm(float u, float v, int freqBase, int octavas, int semilla)
    {
        float suma = 0f, amp = 0.5f, norm = 0f;
        for (int o = 0; o < octavas; o++)
        {
            suma += amp * RuidoTeselable(u, v, freqBase << o, semilla + o * 17);
            norm += amp;
            amp *= 0.5f;
        }
        return suma / norm;
    }

    /// <summary>Crea (una sola vez) un PNG procedural teselable en Materiales y lo importa con mipmaps y wrap Repeat.</summary>
    private static Texture2D TexturaProcedural(string nombre, int n, Func<float, float, int, int, Color> pixel)
    {
        string ruta = CarpetaMateriales + "/" + nombre + ".png";
        Texture2D existente = AssetDatabase.LoadAssetAtPath<Texture2D>(ruta);
        if (existente != null) return existente;

        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                px[y * n + x] = pixel((x + 0.5f) / n, (y + 0.5f) / n, x, y);
        tex.SetPixels(px);
        tex.Apply();
        File.WriteAllBytes(Application.dataPath + "/SolParaTodos/Materiales/" + nombre + ".png", tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(ruta, ImportAssetOptions.ForceUpdate);
        TextureImporter ti = AssetImporter.GetAtPath(ruta) as TextureImporter;
        if (ti != null)
        {
            ti.sRGBTexture = true;
            ti.mipmapEnabled = true;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.filterMode = FilterMode.Trilinear;
            ti.anisoLevel = 4;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(ruta);
    }

    private static Texture2D TexturaGrava()
    {
        return TexturaProcedural("SPT_Grava_Tex_v1", 256, (u, v, x, y) =>
        {
            float m = 0.86f + 0.26f * Fbm(u, v, 6, 4, 11);
            float g = Hash2(x, y, 5);
            if (g > 0.94f) m *= 0.78f;           // piedritas oscuras
            else if (g < 0.05f) m *= 1.10f;      // piedritas claras
            return new Color(Mathf.Clamp01(0.82f * m), Mathf.Clamp01(0.77f * m), Mathf.Clamp01(0.66f * m), 1f);
        });
    }

    private static Texture2D TexturaTierra()
    {
        return TexturaProcedural("SPT_Tierra_Tex_v1", 256, (u, v, x, y) =>
        {
            float m = 0.80f + 0.40f * Fbm(u, v, 5, 4, 23);
            float hierba = Mathf.SmoothStep(0.45f, 0.75f, Fbm(u, v, 9, 3, 41)); // manchas de hierba seca
            Color tierra = new Color(0.60f, 0.52f, 0.37f);
            Color seca = new Color(0.46f, 0.47f, 0.24f);
            Color c = Color.Lerp(tierra, seca, hierba) * m;
            if (Hash2(x, y, 9) > 0.96f) c *= 0.8f;
            c.a = 1f;
            return c;
        });
    }

    /// <summary>Mapa de detalle (multiplica ×2 el color del pasto): claros/oscuros y manchas más secas a gran escala.</summary>
    private static Texture2D TexturaVariacionPasto()
    {
        return TexturaProcedural("SPT_Pasto_Variacion_v1", 256, (u, v, x, y) =>
        {
            float a = Fbm(u, v, 3, 4, 3);
            float b = Fbm(u, v, 4, 3, 71);
            float m = 1f + 0.55f * (a - 0.5f);                 // luz / sombra del césped
            float seco = Mathf.SmoothStep(0.52f, 0.72f, b);    // zonas más amarillentas
            float r = m * (1f + 0.30f * seco);
            float g = m * (1f - 0.04f * seco);
            float bl = m * (1f - 0.35f * seco);
            // El mapa se multiplica ×2 y se guarda en sRGB: 1.0 de multiplicador = 0.5 lineal.
            Func<float, float> cod = k => Mathf.LinearToGammaSpace(Mathf.Clamp01(k * 0.5f));
            return new Color(cod(r), cod(g), cod(bl), 1f);
        });
    }

    // =================================================================== Planos de caminos (compartidos por calles, cerca y entorno)
    private struct PlanoCaminos
    {
        public float hx, hz, px, pz;       // media granja y semiejes del camino perimetral
        public float xSalida0, zEste0;     // dónde nacen los caminos de salida (sur y este)
    }

    private static PlanoCaminos CalcularCaminos(GeoGranja geo)
    {
        var p = new PlanoCaminos();
        p.hx = geo.anchoX * 0.5f; p.hz = geo.largoZ * 0.5f;
        p.px = p.hx + MargenPerimetro; p.pz = p.hz + MargenPerimetro;
        p.xSalida0 = p.px * 0.45f;
        p.zEste0 = p.pz * 0.30f;
        return p;
    }

    private static float XSalida(PlanoCaminos p, float dz) { float k = dz / 25f; return p.xSalida0 + 22f * Mathf.Sin(k * 0.22f) + k * 1.2f; }
    private static float ZEste(PlanoCaminos p, float dx) { float k = dx / 25f; return p.zEste0 + 18f * Mathf.Sin(k * 0.19f + 1.1f) - k * 1.0f; }

    private static List<Vector2> Polilinea(Vector2[] esquinas, bool cerrada, float paso)
    {
        var r = new List<Vector2>();
        int n = esquinas.Length;
        int tramos = cerrada ? n : n - 1;
        for (int i = 0; i < tramos; i++)
        {
            Vector2 a = esquinas[i], b = esquinas[(i + 1) % n];
            int k = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / paso));
            for (int j = 0; j < k; j++) r.Add(Vector2.Lerp(a, b, (float)j / k));
        }
        if (!cerrada) r.Add(esquinas[n - 1]);
        return r;
    }

    private static void TriArriba(List<Vector3> v, List<int> t, int a, int b, int c)
    {
        Vector3 n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
        if (n.y >= 0f) { t.Add(a); t.Add(b); t.Add(c); }
        else { t.Add(a); t.Add(c); t.Add(b); }
    }

    /// <summary>Cinta plana siguiendo una polilínea (en XZ) con ancho y trazo ligeramente irregulares. UV en metros / tile.</summary>
    private static void AgregarTira(List<Vector3> v, List<Vector2> uv, List<int> t, List<Vector2> pts, bool cerrada,
                                    float ancho, float y, float tile, int semilla)
    {
        int n = pts.Count;
        if (n < 2) return;
        int b0 = v.Count;
        for (int i = 0; i < n; i++)
        {
            Vector2 prev = cerrada ? pts[(i - 1 + n) % n] : pts[Mathf.Max(i - 1, 0)];
            Vector2 next = cerrada ? pts[(i + 1) % n] : pts[Mathf.Min(i + 1, n - 1)];
            Vector2 d1 = pts[i] - prev; d1 = d1.sqrMagnitude > 1e-6f ? d1.normalized : (next - pts[i]).normalized;
            Vector2 d2 = next - pts[i]; d2 = d2.sqrMagnitude > 1e-6f ? d2.normalized : d1;
            Vector2 tan = d1 + d2; tan = tan.sqrMagnitude > 1e-6f ? tan.normalized : d2;
            Vector2 nor = new Vector2(-tan.y, tan.x);
            float miter = 1f / Mathf.Max(0.5f, Vector2.Dot(nor, new Vector2(-d2.y, d2.x)));
            float w = ancho * 0.5f * (1f + 0.10f * (Hash2(i, 0, semilla) - 0.5f) * 2f) * miter;
            Vector2 c = pts[i] + nor * ((Hash2(i, 1, semilla) - 0.5f) * 0.7f);
            Vector2 L = c + nor * w, R = c - nor * w;
            v.Add(new Vector3(L.x, y, L.y)); uv.Add(L / tile);
            v.Add(new Vector3(R.x, y, R.y)); uv.Add(R / tile);
        }
        int tramos = cerrada ? n : n - 1;
        for (int i = 0; i < tramos; i++)
        {
            int j = (i + 1) % n;
            int a = b0 + i * 2, b = a + 1, c2 = b0 + j * 2, d = c2 + 1;
            TriArriba(v, t, a, b, c2);
            TriArriba(v, t, b, d, c2);
        }
    }

    private static void AgregarCaja(List<Vector3> v, List<int> t, Vector3 centro, Vector3 tam, float yaw)
    {
        Quaternion q = Quaternion.Euler(0f, yaw, 0f);
        Vector3 h = tam * 0.5f;
        var c = new Vector3[8];
        for (int i = 0; i < 8; i++)
            c[i] = centro + q * new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z);
        int[][] caras =
        {
            new[] { 0, 2, 6, 4 }, new[] { 1, 3, 7, 5 }, new[] { 0, 1, 5, 4 },
            new[] { 2, 3, 7, 6 }, new[] { 0, 1, 3, 2 }, new[] { 4, 5, 7, 6 }
        };
        foreach (int[] f in caras)
        {
            int b0 = v.Count;
            Vector3 centroCara = Vector3.zero;
            foreach (int k in f) { v.Add(c[k]); centroCara += c[k] * 0.25f; }
            bool directo = Vector3.Dot(Vector3.Cross(v[b0 + 1] - v[b0], v[b0 + 2] - v[b0]), centroCara - centro) > 0f;
            if (directo) { t.Add(b0); t.Add(b0 + 1); t.Add(b0 + 2); t.Add(b0); t.Add(b0 + 2); t.Add(b0 + 3); }
            else { t.Add(b0); t.Add(b0 + 2); t.Add(b0 + 1); t.Add(b0); t.Add(b0 + 3); t.Add(b0 + 2); }
        }
    }

    private static void AgregarElipsoide(List<Vector3> v, List<int> t, Vector3 c, Vector3 radios)
    {
        const int seg = 8, ani = 4;
        int baseIdx = v.Count;
        for (int j = 0; j <= ani; j++)
        {
            float phi = Mathf.PI * j / ani;
            for (int i = 0; i <= seg; i++)
            {
                float th = 2f * Mathf.PI * i / seg;
                v.Add(c + Vector3.Scale(new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th)), radios));
            }
        }
        for (int j = 0; j < ani; j++)
            for (int i = 0; i < seg; i++)
            {
                int a = baseIdx + j * (seg + 1) + i, b = a + 1, c2 = a + seg + 1, d = c2 + 1;
                t.Add(a); t.Add(b); t.Add(c2);
                t.Add(b); t.Add(d); t.Add(c2);
            }
    }

    // =================================================================== Caminos de tierra / grava (como en la foto de referencia)
    private static void CrearCalles(GeoGranja geo, float sueloY)
    {
        if (geo == null || !geo.valida) return;
        PlanoCaminos p = CalcularCaminos(geo);

        Material grava = CrearMaterial("SPT_Grava", Color.white, Color.black, 0.04f, 0f);
        grava.SetTexture("_BaseMap", TexturaGrava());
        Material tierra = CrearMaterial("SPT_Tierra", Color.white, Color.black, 0.02f, 0f);
        tierra.SetTexture("_BaseMap", TexturaTierra());
        EditorUtility.SetDirty(grava); EditorUtility.SetDirty(tierra);

        var raiz = new GameObject("SPT_Calles");
        Undo.RegisterCreatedObjectUndo(raiz, "Calles");
        raiz.AddComponent<SPTGenerado>();
        raiz.transform.SetPositionAndRotation(geo.campo.position, geo.campo.rotation);

        float yBase = sueloY - geo.campo.position.y;
        float yTierra = yBase + 0.035f, yGrava = yBase + 0.07f;

        var vG = new List<Vector3>(); var uG = new List<Vector2>(); var tG = new List<int>();
        var vT = new List<Vector3>(); var uT = new List<Vector2>(); var tT = new List<int>();

        // Anillo perimetral
        var anillo = Polilinea(new[]
        {
            new Vector2(-p.px, -p.pz), new Vector2(p.px, -p.pz), new Vector2(p.px, p.pz), new Vector2(-p.px, p.pz)
        }, true, 8f);
        AgregarTira(vT, uT, tT, anillo, true, 6.5f + 4f, yTierra, 4f, 1);
        AgregarTira(vG, uG, tG, anillo, true, 6.5f, yGrava, 3f, 2);

        // Camino central norte–sur entre los dos bloques de paneles
        var central = Polilinea(new[] { new Vector2(0f, -p.pz), new Vector2(0f, p.pz) }, false, 8f);
        AgregarTira(vT, uT, tT, central, false, 6.5f + 4f, yTierra, 4f, 3);
        AgregarTira(vG, uG, tG, central, false, 6.5f, yGrava, 3f, 4);

        // Camino de salida hacia el sur (curva suave hasta perderse en el horizonte)
        var sur = new List<Vector2>();
        for (float dz = -3f; dz <= 800f; dz += 25f) sur.Add(new Vector2(XSalida(p, Mathf.Max(0f, dz)), -p.pz - dz));
        AgregarTira(vT, uT, tT, sur, false, 6f + 4f, yTierra, 4f, 5);
        AgregarTira(vG, uG, tG, sur, false, 6f, yGrava, 3f, 6);

        // Camino de salida hacia el este
        var este = new List<Vector2>();
        for (float dx = -3f; dx <= 700f; dx += 25f) este.Add(new Vector2(p.px + dx, ZEste(p, Mathf.Max(0f, dx))));
        AgregarTira(vT, uT, tT, este, false, 5.5f + 4f, yTierra, 4f, 7);
        AgregarTira(vG, uG, tG, este, false, 5.5f, yGrava, 3f, 8);

        CrearObjetoMalla(raiz.transform, "SPT_Tierra_Borde", vT, tT, tierra, false, uT);
        CrearObjetoMalla(raiz.transform, "SPT_Caminos", vG, tG, grava, false, uG);
    }

    // =================================================================== Cerca perimetral + arbustos + árboles
    private static void CrearEntorno(GeoGranja geo, float sueloY)
    {
        if (geo == null || !geo.valida) return;
        PlanoCaminos p = CalcularCaminos(geo);
        Transform campo = geo.campo;

        Material mCerca = CrearMaterial("SPT_Cerca", new Color(0.42f, 0.44f, 0.46f), Color.black, 0.35f, 0.3f);
        Material mArbusto = CrearMaterial("SPT_Arbusto", new Color(0.20f, 0.29f, 0.09f), Color.black, 0.02f, 0f);
        Material mFollaje = CrearMaterial("SPT_Follaje", new Color(0.12f, 0.24f, 0.08f), Color.black, 0.02f, 0f);
        Material mTronco = CrearMaterial("SPT_Tronco", new Color(0.24f, 0.17f, 0.11f), Color.black, 0.02f, 0f);

        var raiz = new GameObject("SPT_Entorno");
        Undo.RegisterCreatedObjectUndo(raiz, "Entorno");
        raiz.AddComponent<SPTGenerado>();

        Func<float, float, Vector3> aMundo = (x, z) => { Vector3 w = campo.TransformPoint(new Vector3(x, 0f, z)); w.y = sueloY; return w; };
        float yaw = campo.eulerAngles.y;

        // ---- Cerca (postes cada 5 m + 4 travesaños); se abre donde cruzan los caminos de salida
        float fx = p.px + 6f, fz = p.pz + 6f;
        float gxS = XSalida(p, 6f), gzE = ZEste(p, 6f);
        const float medioPortón = 4.5f;
        var vC = new List<Vector3>(); var tC = new List<int>();
        Action<Vector2, Vector2> tramo = (a, b) =>
        {
            float largo = Vector2.Distance(a, b);
            if (largo < 1f) return;
            Vector2 d = (b - a) / largo;
            int postes = Mathf.Max(1, Mathf.CeilToInt(largo / 5f));
            for (int i = 0; i <= postes; i++)
            {
                Vector2 q = Vector2.Lerp(a, b, (float)i / postes);
                Vector3 w = aMundo(q.x, q.y);
                AgregarCaja(vC, tC, w + Vector3.up * 0.95f, new Vector3(0.1f, 1.9f, 0.1f), yaw);
            }
            Vector2 m = (a + b) * 0.5f;
            float ang = yaw + Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg; // eje Z local = largo
            foreach (float h in new[] { 0.35f, 0.85f, 1.35f, 1.85f })
                AgregarCaja(vC, tC, aMundo(m.x, m.y) + Vector3.up * h, new Vector3(0.035f, 0.035f, largo), ang);
        };
        tramo(new Vector2(-fx, fz), new Vector2(fx, fz));                                    // norte
        tramo(new Vector2(-fx, -fz), new Vector2(-fx, fz));                                  // oeste
        tramo(new Vector2(-fx, -fz), new Vector2(gxS - medioPortón, -fz));                   // sur (izq.)
        tramo(new Vector2(gxS + medioPortón, -fz), new Vector2(fx, -fz));                    // sur (der.)
        tramo(new Vector2(fx, -fz), new Vector2(fx, gzE - medioPortón));                     // este (abajo)
        tramo(new Vector2(fx, gzE + medioPortón), new Vector2(fx, fz));                      // este (arriba)
        CrearObjetoMalla(raiz.transform, "SPT_Cerca", vC, tC, mCerca, false);

        // ---- Arbustos y árboles dispersos fuera de la cerca
        var rnd = new System.Random(20240611);
        Func<double> r01 = () => rnd.NextDouble();
        Func<float, float, bool> libre = (x, z) =>
        {
            if (Mathf.Abs(x) < fx + 9f && Mathf.Abs(z) < fz + 9f) return false;                         // dentro / pegado a la cerca
            if (z < -p.pz && Mathf.Abs(x - XSalida(p, Mathf.Max(0f, -p.pz - z))) < 12f) return false;  // camino sur
            if (x > p.px && Mathf.Abs(z - ZEste(p, Mathf.Max(0f, x - p.px))) < 12f) return false;      // camino este
            return true;
        };

        var vB = new List<Vector3>(); var tB = new List<int>();
        var vF = new List<Vector3>(); var tF = new List<int>();
        var vTr = new List<Vector3>(); var tTr = new List<int>();

        int grupos = 0, intentos = 0;
        float alcance = Mathf.Max(fx, fz) + 380f;
        while (grupos < 40 && intentos++ < 800)
        {
            float cx = (float)(r01() * 2.0 - 1.0) * alcance, cz = (float)(r01() * 2.0 - 1.0) * alcance;
            if (!libre(cx, cz)) continue;
            grupos++;
            int cuantos = 2 + (int)(r01() * 6.0);
            for (int k = 0; k < cuantos; k++)
            {
                float x = cx + (float)(r01() * 2.0 - 1.0) * 7f, z = cz + (float)(r01() * 2.0 - 1.0) * 7f;
                if (!libre(x, z)) continue;
                float r = 0.8f + (float)r01() * 1.5f;
                Vector3 w = aMundo(x, z);
                AgregarElipsoide(vB, tB, w + Vector3.up * (r * 0.45f), new Vector3(r, r * 0.65f, r * (0.8f + (float)r01() * 0.4f)));
            }
        }

        int arboles = 0; intentos = 0;
        while (arboles < 18 && intentos++ < 600)
        {
            float cx = (float)(r01() * 2.0 - 1.0) * (alcance + 120f), cz = (float)(r01() * 2.0 - 1.0) * (alcance + 120f);
            if (!libre(cx, cz)) continue;
            arboles++;
            float h = 4.5f + (float)r01() * 3f;
            Vector3 w = aMundo(cx, cz);
            AgregarCilindro(vTr, tTr, w, 0.22f, h * 0.55f);
            float rc = 2.4f + (float)r01() * 1.6f;
            AgregarElipsoide(vF, tF, w + Vector3.up * (h * 0.8f), new Vector3(rc, rc * 0.85f, rc));
            AgregarElipsoide(vF, tF, w + Vector3.up * (h * 0.62f) + new Vector3(rc * 0.45f, 0f, 0f), new Vector3(rc * 0.7f, rc * 0.6f, rc * 0.7f));
        }

        CrearObjetoMalla(raiz.transform, "SPT_Arbustos", vB, tB, mArbusto, false);
        CrearObjetoMalla(raiz.transform, "SPT_Follaje", vF, tF, mFollaje, false);
        CrearObjetoMalla(raiz.transform, "SPT_Troncos", vTr, tTr, mTronco, false);
    }

    // =================================================================== Inversores (casetas blancas junto al camino central) + LED
    private static Renderer[] CrearInversores(GeoGranja geo, Transform[] filas, Bounds parque, float sueloY)
    {
        var leds = new List<Renderer>();
        if (filas.Length == 0) return leds.ToArray();

        Material matCaja = CrearMaterial("SPT_Inversor", new Color(0.92f, 0.93f, 0.94f), Color.black, 0.45f, 0.05f);
        Material matTrafo = CrearMaterial("SPT_Transformador", new Color(0.78f, 0.80f, 0.81f), Color.black, 0.4f, 0.1f);
        Material matBase = CrearMaterial("SPT_BaseConcreto", new Color(0.55f, 0.55f, 0.53f), Color.black, 0.05f, 0f);
        Material matLed = CrearMaterial("SPT_LED_Estado", new Color(0.1f, 0.6f, 0.25f), new Color(0.1f, 1f, 0.35f) * 1.5f, 0.8f, 0f);

        var raiz = new GameObject("SPT_Inversores");
        Undo.RegisterCreatedObjectUndo(raiz, "Crear inversores");
        raiz.AddComponent<SPTGenerado>();

        bool rejilla = geo != null && geo.valida;
        if (rejilla) CrearCalles(geo, sueloY);

        // Posiciones (mundo) y orientación de cada estación.
        var puestos = new List<KeyValuePair<Vector3, Quaternion>>();
        if (rejilla)
        {
            float hz = geo.largoZ * 0.5f;
            int n = Mathf.Max(2, Mathf.RoundToInt(2f * hz / 45f));
            for (int k = 0; k < n; k++)
            {
                float z = Mathf.Lerp(-hz + 22f, hz - 22f, (float)k / (n - 1));
                foreach (float s in new[] { -1f, 1f })
                {
                    Vector3 pos = geo.campo.TransformPoint(new Vector3(s * 5.7f, 0f, z));
                    Quaternion rot = geo.campo.rotation * Quaternion.LookRotation(new Vector3(-s, 0f, 0f), Vector3.up);
                    puestos.Add(new KeyValuePair<Vector3, Quaternion>(pos, rot));
                }
            }
        }
        else
        {
            foreach (Transform f in filas)
            {
                Bounds b = LimitesDe(f);
                if (b.size.sqrMagnitude < 0.01f) continue;
                bool largoX = b.extents.x >= b.extents.z;
                Vector3 eje = largoX ? Vector3.right : Vector3.forward;
                puestos.Add(new KeyValuePair<Vector3, Quaternion>(b.center + eje * ((largoX ? b.extents.x : b.extents.z) + 1.0f),
                                                                  Quaternion.LookRotation(-eje, Vector3.up)));
            }
        }

        int idx = 0;
        foreach (var pz in puestos)
        {
            idx++;
            Vector3 pos = pz.Key; pos.y = sueloY;
            Quaternion rot = pz.Value;
            Vector3 lado = rot * Vector3.right;   // a lo largo del camino

            var baseGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(baseGo, "Base");
            baseGo.name = "SPT_Base_" + idx;
            Object.DestroyImmediate(baseGo.GetComponent<Collider>());
            baseGo.transform.SetParent(raiz.transform, false);
            baseGo.transform.SetPositionAndRotation(pos + lado * 1.1f + Vector3.up * 0.03f, rot);
            baseGo.transform.localScale = new Vector3(5.4f, 0.06f, 2.6f);
            baseGo.GetComponent<MeshRenderer>().sharedMaterial = matBase;
            baseGo.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            var caja = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(caja, "Inversor");
            caja.name = "SPT_Inversor_" + idx.ToString("00");
            Object.DestroyImmediate(caja.GetComponent<Collider>());
            caja.transform.SetParent(raiz.transform, false);
            caja.transform.SetPositionAndRotation(pos - lado * 0.2f + Vector3.up * (0.06f + 0.95f), rot);
            caja.transform.localScale = new Vector3(1.5f, 1.9f, 0.7f);
            caja.GetComponent<MeshRenderer>().sharedMaterial = matCaja;

            var trafo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(trafo, "Transformador");
            trafo.name = "SPT_Transformador_" + idx.ToString("00");
            Object.DestroyImmediate(trafo.GetComponent<Collider>());
            trafo.transform.SetParent(raiz.transform, false);
            trafo.transform.SetPositionAndRotation(pos + lado * 2.3f + Vector3.up * (0.06f + 0.75f), rot);
            trafo.transform.localScale = new Vector3(1.9f, 1.5f, 1.3f);
            trafo.GetComponent<MeshRenderer>().sharedMaterial = matTrafo;

            var led = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Undo.RegisterCreatedObjectUndo(led, "LED");
            led.name = "LED_Estado";
            Object.DestroyImmediate(led.GetComponent<Collider>());
            led.transform.SetParent(caja.transform, false);
            led.transform.localPosition = new Vector3(0.28f, 0.32f, 0.5f);
            led.transform.localScale = new Vector3(0.16f / 1.5f, 0.16f / 1.9f, 0.16f / 0.7f);
            MeshRenderer mr = led.GetComponent<MeshRenderer>();
            mr.sharedMaterial = matLed;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            leds.Add(mr);
        }
        return leds.ToArray();
    }

    // =================================================================== Volume nocturno (Crepúsculo)
    private static Volume CrearVolumenNocturno()
    {
        string ruta = CarpetaSettings + "/SPT_PerfilNocturno.asset";
        // Se recrea siempre desde cero: un perfil viejo o dañado hacía fallar AddObjectToAsset.
        AssetDatabase.DeleteAsset(ruta);
        VolumeProfile perfil = ScriptableObject.CreateInstance<VolumeProfile>();
        perfil.name = "SPT_PerfilNocturno";
        AssetDatabase.CreateAsset(perfil, ruta);

        // Valores v2: NO oscurece (el relleno azul ya da la atmósfera); el Bloom hace brillar los emisivos.
        Bloom bloom; if (!perfil.TryGet(out bloom)) bloom = perfil.Add<Bloom>(true);
        bloom.intensity.Override(2.4f);
        bloom.threshold.Override(0.9f);
        bloom.scatter.Override(0.72f);
        bloom.tint.Override(new Color(0.70f, 0.84f, 1f));

        ColorAdjustments ajustes; if (!perfil.TryGet(out ajustes)) ajustes = perfil.Add<ColorAdjustments>(true);
        ajustes.postExposure.Override(0.25f);
        ajustes.colorFilter.Override(new Color(0.80f, 0.89f, 1f));
        ajustes.saturation.Override(-8f);
        ajustes.contrast.Override(10f);

        Vignette vineta; if (!perfil.TryGet(out vineta)) vineta = perfil.Add<Vignette>(true);
        vineta.intensity.Override(0.30f);
        vineta.smoothness.Override(0.5f);
        vineta.color.Override(new Color(0.01f, 0.02f, 0.07f));

        foreach (VolumeComponent comp in perfil.components)
        {
            comp.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(comp))) AssetDatabase.AddObjectToAsset(comp, perfil);
            EditorUtility.SetDirty(comp);
        }
        EditorUtility.SetDirty(perfil);
        AssetDatabase.SaveAssets();

        var go = new GameObject("SPT_VolumenNocturno");
        Undo.RegisterCreatedObjectUndo(go, "Volume nocturno");
        go.AddComponent<SPTGenerado>();
        Volume v = go.AddComponent<Volume>();
        v.isGlobal = true;
        v.priority = 10f;
        v.weight = 0f;
        v.sharedProfile = perfil;
        return v;
    }

    // =================================================================== EventSystem
    private static void AsegurarEventSystem(List<string> informe)
    {
        EventSystem es = Todos<EventSystem>().FirstOrDefault();
        if (es == null)
        {
            var go = new GameObject("EventSystem");
            Undo.RegisterCreatedObjectUndo(go, "EventSystem");
            go.AddComponent<SPTGenerado>();
            es = go.AddComponent<EventSystem>();
            informe.Add("EventSystem creado con InputSystemUIInputModule.");
        }

        // El proyecto usa solo el nuevo Input System: StandaloneInputModule lanzaría excepciones.
        foreach (StandaloneInputModule viejo in es.GetComponents<StandaloneInputModule>())
            Undo.DestroyObjectImmediate(viejo);

        InputSystemUIInputModule modulo = es.GetComponent<InputSystemUIInputModule>();
        if (modulo == null) modulo = Undo.AddComponent<InputSystemUIInputModule>(es.gameObject);
        if (modulo.actionsAsset == null) modulo.AssignDefaultActions();
    }

    // =================================================================== SPRITES PROCEDURALES
    private static float Cob(float d) { return Mathf.Clamp01(0.5f - d); }

    private static float SdCajaRedonda(float px, float py, float hx, float hy, float r)
    {
        float qx = Mathf.Abs(px) - (hx - r), qy = Mathf.Abs(py) - (hy - r);
        float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
        return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
    }

    private static float SdSegmento(float px, float py, float ax, float ay, float bx, float by)
    {
        float pax = px - ax, pay = py - ay, bax = bx - ax, bay = by - ay;
        float h = Mathf.Clamp01((pax * bax + pay * bay) / (bax * bax + bay * bay));
        float dx = pax - bax * h, dy = pay - bay * h;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    private static Sprite CrearSprite(string nombre, int ancho, int alto, Func<float, float, float> alfa, Vector4 borde)
    {
        string ruta = CarpetaSprites + "/" + nombre + ".png";
        var tex = new Texture2D(ancho, alto, TextureFormat.RGBA32, false);
        var px = new Color32[ancho * alto];
        for (int y = 0; y < alto; y++)
            for (int x = 0; x < ancho; x++)
            {
                float a = Mathf.Clamp01(alfa(x + 0.5f, y + 0.5f));
                px[y * ancho + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        File.WriteAllBytes(ruta, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(ruta, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        TextureImporter ti = AssetImporter.GetAtPath(ruta) as TextureImporter;
        if (ti != null)
        {
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = 100f;
            ti.spriteBorder = borde;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.filterMode = FilterMode.Bilinear;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.maxTextureSize = 256;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(ruta);
    }

    private static void CrearSprites()
    {
        _sprVidrio = CrearSprite("SPT_Vidrio", 64, 64, (x, y) => Cob(SdCajaRedonda(x - 32f, y - 32f, 32f, 32f, 22f)), new Vector4(24, 24, 24, 24));
        _sprBorde = CrearSprite("SPT_Borde", 64, 64, (x, y) =>
        {
            const float w = 1.5f;
            float d = SdCajaRedonda(x - 32f, y - 32f, 32f, 32f, 22f);
            return Mathf.Clamp01(w * 0.5f + 0.5f - Mathf.Abs(d + w * 0.5f));
        }, new Vector4(24, 24, 24, 24));
        _sprSombra = CrearSprite("SPT_Sombra", 128, 128, (x, y) =>
        {
            float d = SdCajaRedonda(x - 64f, y - 64f, 36f, 36f, 22f);
            float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-10f, 26f, d));
            return Mathf.Pow(1f - t, 1.4f);
        }, new Vector4(60, 60, 60, 60));
        _sprPastilla = CrearSprite("SPT_Pastilla", 64, 16, (x, y) => Cob(SdCajaRedonda(x - 32f, y - 8f, 32f, 8f, 8f)), new Vector4(8, 8, 8, 8));
        _sprCirculo = CrearSprite("SPT_Circulo", 128, 128, (x, y) => Cob(Mathf.Sqrt((x - 64f) * (x - 64f) + (y - 64f) * (y - 64f)) - 62f), Vector4.zero);
        _sprAroCirculo = CrearSprite("SPT_AroCirculo", 128, 128, (x, y) =>
        {
            float d = Mathf.Sqrt((x - 64f) * (x - 64f) + (y - 64f) * (y - 64f)) - 62f;
            return Mathf.Clamp01(1.5f + 0.5f - Mathf.Abs(d + 1.5f));
        }, Vector4.zero);
        _sprDegradado = CrearSprite("SPT_Degradado", 4, 128, (x, y) => 1f - y / 127f, Vector4.zero);

        _sprLuna = CrearSprite("SPT_IconoLuna", 128, 128, (x, y) =>
        {
            float a1 = Cob(Mathf.Sqrt((x - 60f) * (x - 60f) + (y - 64f) * (y - 64f)) - 40f);
            float a2 = Cob(Mathf.Sqrt((x - 84f) * (x - 84f) + (y - 74f) * (y - 74f)) - 36f);
            return Mathf.Min(a1, 1f - a2);
        }, Vector4.zero);
        _sprSol = CrearSprite("SPT_IconoSol", 128, 128, (x, y) =>
        {
            float a = Cob(Mathf.Sqrt((x - 64f) * (x - 64f) + (y - 64f) * (y - 64f)) - 24f);
            for (int k = 0; k < 8; k++)
            {
                float ang = k * Mathf.PI / 4f;
                float cx = Mathf.Cos(ang), cy = Mathf.Sin(ang);
                float d = SdSegmento(x, y, 64f + cx * 38f, 64f + cy * 38f, 64f + cx * 54f, 64f + cy * 54f) - 4.5f;
                a = Mathf.Max(a, Cob(d));
            }
            return a;
        }, Vector4.zero);
        _sprMenu = CrearSprite("SPT_IconoMenu", 128, 128, (x, y) =>
        {
            float a = 0f;
            foreach (float off in new[] { 30f, 0f, -30f })
                a = Mathf.Max(a, Cob(SdCajaRedonda(x - 64f, y - (64f + off), 42f, 6f, 6f)));
            return a;
        }, Vector4.zero);
        _sprChevron = CrearSprite("SPT_IconoChevron", 128, 128, (x, y) =>
        {
            float d1 = SdSegmento(x, y, 34f, 78f, 64f, 48f);
            float d2 = SdSegmento(x, y, 64f, 48f, 94f, 78f);
            return Cob(Mathf.Min(d1, d2) - 7f);
        }, Vector4.zero);
        _sprInfo = CrearSprite("SPT_IconoInfo", 128, 128, (x, y) =>
        {
            float dist = Mathf.Sqrt((x - 64f) * (x - 64f) + (y - 64f) * (y - 64f));
            float aro = Cob(Mathf.Abs(dist - 54f) - 4f);
            float punto = Cob(Mathf.Sqrt((x - 64f) * (x - 64f) + (y - 92f) * (y - 92f)) - 7f);
            float barra = Cob(SdCajaRedonda(x - 64f, y - 52f, 7f, 22f, 6f));
            return Mathf.Max(aro, Mathf.Max(punto, barra));
        }, Vector4.zero);
    }

    // =================================================================== FUENTES (Barlow → TextMeshPro SDF)
    private const string CaracteresFuente =
        " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~" +
        "áéíóúüñÁÉÍÓÚÜÑ¿¡°²³µ·•—–…×±≈º«»“”‘’€ ";

    private static TMP_FontAsset FuenteDe(Peso p)
    {
        TMP_FontAsset f = _fuentes[(int)p];
        return f != null ? f : _fuenteBase;
    }

    private static void CargarFuentes(List<string> informe)
    {
        _fuenteBase = TMP_Settings.defaultFontAsset;
        string[] archivos = { "Barlow-Light", "Barlow-Regular", "Barlow-Medium", "Barlow-SemiBold", "Barlow-Bold" };
        int ok = 0;
        for (int i = 0; i < archivos.Length; i++)
        {
            _fuentes[i] = null;
            try
            {
                string rutaAsset = CarpetaFuentes + "/" + archivos[i] + " SDF.asset";
                TMP_FontAsset existente = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(rutaAsset);
                if (existente != null) { _fuentes[i] = existente; ok++; continue; }

                Font ttf = AssetDatabase.LoadAssetAtPath<Font>(CarpetaFuentes + "/" + archivos[i] + ".ttf");
                if (ttf == null) continue;

                TMP_FontAsset fa = TMP_FontAsset.CreateFontAsset(ttf, 72, 7, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                if (fa == null) continue;
                fa.name = archivos[i] + " SDF";
                string faltan;
                fa.TryAddCharacters(CaracteresFuente, out faltan);
                // Atlas ya horneado: modo Static (sin rasterizar en el dispositivo). Los glifos raros salen de la fuente TMP por defecto.
                fa.atlasPopulationMode = AtlasPopulationMode.Static;
                if (_fuenteBase != null && fa.fallbackFontAssetTable != null && !fa.fallbackFontAssetTable.Contains(_fuenteBase))
                    fa.fallbackFontAssetTable.Add(_fuenteBase);

                AssetDatabase.CreateAsset(fa, rutaAsset);
                if (fa.material != null) { fa.material.name = archivos[i] + " Material"; AssetDatabase.AddObjectToAsset(fa.material, fa); }
                if (fa.atlasTextures != null)
                    for (int a = 0; a < fa.atlasTextures.Length; a++)
                    {
                        Texture2D at = fa.atlasTextures[a];
                        if (at == null) continue;
                        at.name = archivos[i] + " Atlas" + (a == 0 ? "" : " " + a);
                        AssetDatabase.AddObjectToAsset(at, fa);
                    }
                EditorUtility.SetDirty(fa);
                AssetDatabase.SaveAssetIfDirty(fa);
                _fuentes[i] = fa;
                ok++;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Sol Para Todos] No se pudo crear la fuente " + archivos[i] + ": " + e.Message);
            }
        }

        // «<b>» en los textos largos usa Barlow Bold (tabla de pesos de la fuente Regular).
        try
        {
            TMP_FontAsset reg = _fuentes[1];
            if (reg != null && reg.fontWeightTable != null && reg.fontWeightTable.Length > 7)
            {
                reg.fontWeightTable[4].regularTypeface = reg;
                if (_fuentes[3] != null) reg.fontWeightTable[6].regularTypeface = _fuentes[3];
                if (_fuentes[4] != null) reg.fontWeightTable[7].regularTypeface = _fuentes[4];
                EditorUtility.SetDirty(reg);
                AssetDatabase.SaveAssetIfDirty(reg);
            }
        }
        catch (Exception e) { Debug.LogWarning("[Sol Para Todos] Tabla de pesos de fuente: " + e.Message); }

        informe.Add(ok == archivos.Length
            ? "Fuentes Barlow (Light/Regular/Medium/SemiBold/Bold) listas como TMP SDF."
            : "Aviso: fuentes Barlow disponibles " + ok + "/" + archivos.Length + " (las que faltan usan la fuente TMP por defecto).");
    }

    private static void CargarRecursosUI(List<string> informe)
    {
        CrearSprites();
        CargarFuentes(informe);
    }

    private static void AsegurarCarpetas()
    {
        string[] carpetas =
        {
            CarpetaRaiz, CarpetaMateriales, CarpetaSettings, CarpetaMallas, CarpetaFuentes,
            "Assets/SolParaTodos/UI", CarpetaSprites
        };
        foreach (string c in carpetas)
        {
            if (AssetDatabase.IsValidFolder(c)) continue;
            string padre = Path.GetDirectoryName(c).Replace('\\', '/');
            AssetDatabase.CreateFolder(padre, Path.GetFileName(c));
        }
    }

    // =================================================================== PRIMITIVAS DE UI
    private static RectTransform UI(string nombre, Transform padre)
    {
        var go = new GameObject(nombre, typeof(RectTransform));
        go.layer = CapaUI;
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(padre, false);
        return rt;
    }

    private static void Estirar(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void Margenes(RectTransform rt, float izq, float abajo, float der, float arriba)
    {
        rt.offsetMin = new Vector2(izq, abajo);
        rt.offsetMax = new Vector2(-der, -arriba);
    }

    private static Image Imagen(Transform padre, string nombre, Sprite spr, Color color, Image.Type tipo, bool raycast, float multiplicador = 1f)
    {
        RectTransform rt = UI(nombre, padre);
        Estirar(rt);
        rt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true; // las capas del vidrio no entran en los LayoutGroup del padre
        Image i = rt.gameObject.AddComponent<Image>();
        i.sprite = spr;
        i.type = spr == null ? Image.Type.Simple : tipo;
        i.color = color;
        i.raycastTarget = raycast;
        if (tipo == Image.Type.Sliced) i.pixelsPerUnitMultiplier = multiplicador;
        return i;
    }

    /// <summary>Panel «vidrio»: sombra suave (opcional) + fondo translúcido + borde finísimo. Emula el desenfoque con translucidez.</summary>
    private static RectTransform Vidrio(string nombre, Transform padre, Color fondo, float radio, bool borde, Color colorBorde, bool sombra, bool raycast)
    {
        RectTransform raiz = UI(nombre, padre);
        float mult = 22f / Mathf.Max(4f, radio);
        if (sombra)
        {
            Image s = Imagen(raiz, "Sombra", _sprSombra, new Color(0f, 0f, 0f, 0.38f), Image.Type.Sliced, false, 1f);
            s.rectTransform.offsetMin = new Vector2(-28f, -38f);
            s.rectTransform.offsetMax = new Vector2(28f, 18f);
        }
        Imagen(raiz, "Fondo", _sprVidrio, fondo, Image.Type.Sliced, raycast, mult);
        if (borde) Imagen(raiz, "Borde", _sprBorde, colorBorde, Image.Type.Sliced, false, mult);
        return raiz;
    }

    private static RectTransform Vidrio(string nombre, Transform padre, Color fondo)
    {
        return Vidrio(nombre, padre, fondo, 22f, true, CBordeCyan, false, true);
    }

    private static TextMeshProUGUI Texto(Transform padre, string nombre, string contenido, float tam, Color color,
                                         TextAlignmentOptions alineacion, Peso peso = Peso.Normal, float espaciado = 0f, bool unaLinea = false)
    {
        RectTransform rt = UI(nombre, padre);
        TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset f = FuenteDe(peso);
        if (f != null) t.font = f;
        t.text = contenido;
        t.fontSize = tam;
        t.color = color;
        t.alignment = alineacion;
        t.characterSpacing = espaciado;
        t.raycastTarget = false;
        t.richText = true;
        t.textWrappingMode = unaLinea ? TextWrappingModes.NoWrap : TextWrappingModes.Normal;
        t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }

    private static void AutoTam(TMP_Text t, float min, float max)
    {
        t.enableAutoSizing = true;
        t.fontSizeMin = min;
        t.fontSizeMax = max;
    }

    private static void ConfigurarBoton(Button b)
    {
        ColorBlock cb = b.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = Color.white;
        cb.pressedColor = new Color(0.74f, 0.74f, 0.74f, 1f);
        cb.selectedColor = Color.white;
        cb.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.5f);
        cb.fadeDuration = 0.07f;
        b.colors = cb;
        Navigation nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
    }

    private static Button BotonVidrio(Transform padre, string nombre, string etiqueta, float tam, Color fondo, Color colorTexto,
                                      Peso peso, float radio, bool borde, Color colorBorde, bool sombra)
    {
        RectTransform raiz = Vidrio(nombre, padre, fondo, radio, borde, colorBorde, sombra, true);
        Button b = raiz.gameObject.AddComponent<Button>();
        b.targetGraphic = raiz.Find("Fondo").GetComponent<Image>();
        ConfigurarBoton(b);

        TextMeshProUGUI t = Texto(raiz, "Etiqueta", etiqueta, tam, colorTexto, TextAlignmentOptions.Center, peso, 0f, true);
        AutoTam(t, Mathf.Max(14f, tam * 0.6f), tam);
        Estirar(t.rectTransform);
        Margenes(t.rectTransform, 12f, 4f, 12f, 4f);
        raiz.gameObject.AddComponent<SPTButtonFeedback>();
        return b;
    }

    private static Button BotonIcono(Transform padre, string nombre, Sprite icono, float tamIcono, Color tinte, Color fondo, float radio, out Image imagenIcono)
    {
        RectTransform raiz = Vidrio(nombre, padre, fondo, radio, true, CBordeCyan, false, true);
        Button b = raiz.gameObject.AddComponent<Button>();
        b.targetGraphic = raiz.Find("Fondo").GetComponent<Image>();
        ConfigurarBoton(b);

        RectTransform ri = UI("Icono", raiz);
        ri.anchorMin = ri.anchorMax = new Vector2(0.5f, 0.5f);
        ri.sizeDelta = new Vector2(tamIcono, tamIcono);
        imagenIcono = ri.gameObject.AddComponent<Image>();
        imagenIcono.sprite = icono;
        imagenIcono.color = tinte;
        imagenIcono.raycastTarget = false;
        imagenIcono.preserveAspect = true;
        raiz.gameObject.AddComponent<SPTButtonFeedback>();
        return b;
    }

    private static void Altura(GameObject go, float alto)
    {
        LayoutElement le = go.GetComponent<LayoutElement>();
        if (le == null) le = go.AddComponent<LayoutElement>();
        le.preferredHeight = alto;
        le.minHeight = alto;
    }

    private static void Tamano(GameObject go, float ancho, float alto)
    {
        LayoutElement le = go.GetComponent<LayoutElement>();
        if (le == null) le = go.AddComponent<LayoutElement>();
        le.preferredWidth = ancho; le.minWidth = ancho;
        le.preferredHeight = alto; le.minHeight = alto;
    }

    private static void Separador(RectTransform padre, float alto)
    {
        RectTransform s = UI("Separador", padre);
        Altura(s.gameObject, alto);
    }

    /// <summary>Línea finísima (1 px de canvas) centrada en una fila de altura fija.</summary>
    private static void Linea(RectTransform padre, float alturaFila, Color color)
    {
        RectTransform fila = UI("Linea", padre);
        Altura(fila.gameObject, alturaFila);
        RectTransform l = UI("Trazo", fila);
        l.anchorMin = new Vector2(0f, 0.5f); l.anchorMax = new Vector2(1f, 0.5f);
        l.sizeDelta = new Vector2(0f, 1f);
        Image i = l.gameObject.AddComponent<Image>();
        i.color = color;
        i.raycastTarget = false;
    }

    private static VerticalLayoutGroup LayoutVertical(GameObject go, RectOffset padding, float espacio, bool ajustarAlto)
    {
        VerticalLayoutGroup v = go.AddComponent<VerticalLayoutGroup>();
        v.padding = padding;
        v.spacing = espacio;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        if (ajustarAlto)
        {
            ContentSizeFitter f = go.AddComponent<ContentSizeFitter>();
            f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }
        return v;
    }

    // =================================================================== CONSTRUCCIÓN DE LA UI
    private static void ConstruirUI(SolarFieldController ctrl)
    {
        var goCanvas = new GameObject("SPT_UI_Canvas");
        Undo.RegisterCreatedObjectUndo(goCanvas, "Canvas");
        goCanvas.layer = CapaUI;
        goCanvas.AddComponent<SPTGenerado>();
        Canvas canvas = goCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        canvas.pixelPerfect = false;
        CanvasScaler scaler = goCanvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0f;
        goCanvas.AddComponent<GraphicRaycaster>();

        RectTransform segura = UI("SafeArea", goCanvas.transform);
        Estirar(segura);
        SPTResponsiveCanvas responsivo = goCanvas.AddComponent<SPTResponsiveCanvas>();
        responsivo.areaSegura = segura;

        // ---- Área de mirada (detrás de todo)
        RectTransform mirada = UI("AreaMirada_Arrastre", segura);
        Estirar(mirada);
        Image imgMirada = mirada.gameObject.AddComponent<Image>();
        imgMirada.color = new Color(0f, 0f, 0f, 0f);
        ctrl.areaMirada = mirada.gameObject.AddComponent<SPTTouchLookArea>();

        ctrl.spriteLuna = _sprLuna;
        ctrl.spriteSol = _sprSol;

        // ---- HUD
        RectTransform hud = UI("HUD_Simulacion", segura);
        Estirar(hud);
        ctrl.panelHUD = hud.gameObject;
        ConstruirBanner(hud, ctrl);
        ConstruirTelemetria(hud, ctrl);
        ConstruirRielControles(hud, ctrl);
        ConstruirBarraHora(hud, ctrl);
        ConstruirJoystick(hud, ctrl);
        hud.gameObject.SetActive(false);

        // ---- Fichas técnicas (modal)
        ConstruirFichas(segura, ctrl);

        // ---- Menú de inicio y Acerca de
        ConstruirMenu(segura, ctrl);
        ConstruirAcerca(segura, ctrl);
    }

    // ----------------------------------------------------------------- Banner de ayuda (se oculta solo)
    private static void ConstruirBanner(RectTransform hud, SolarFieldController ctrl)
    {
        RectTransform banner = Vidrio("BannerGuia", hud, CVidrio, 20f, true, CBordeCyan, true, true);
        banner.anchorMin = new Vector2(0f, 0f);
        banner.anchorMax = new Vector2(1f, 0f);
        banner.pivot = new Vector2(0.5f, 0f);
        banner.offsetMin = new Vector2(24f, 212f);
        banner.offsetMax = new Vector2(-24f, 296f);
        ctrl.bannerGuia = banner.gameObject;

        RectTransform acento = UI("Acento", banner);
        acento.anchorMin = new Vector2(0f, 0f); acento.anchorMax = new Vector2(0f, 1f);
        acento.pivot = new Vector2(0f, 0.5f);
        acento.offsetMin = new Vector2(0f, 16f); acento.offsetMax = new Vector2(4f, -16f);
        Image ia = acento.gameObject.AddComponent<Image>();
        ia.color = CCyan; ia.raycastTarget = false;

        TextMeshProUGUI t = Texto(banner, "Texto",
            "<b><color=#00D1FF>Simulación solar:</color></b> mueve la hora, cambia de vista y observa cómo el ángulo del sol define la energía del parque.",
            23f, CTexto, TextAlignmentOptions.MidlineLeft, Peso.Normal);
        AutoTam(t, 16f, 23f);
        Estirar(t.rectTransform);
        Margenes(t.rectTransform, 32f, 8f, 84f, 8f);

        Button cerrar = BotonVidrio(banner, "Cerrar", "×", 34f, new Color32(255, 255, 255, 16), CTextoSec, Peso.Media, 14f, false, CBordeNeutro, false);
        RectTransform rc = (RectTransform)cerrar.transform;
        rc.anchorMin = rc.anchorMax = new Vector2(1f, 0.5f);
        rc.pivot = new Vector2(1f, 0.5f);
        rc.sizeDelta = new Vector2(56f, 56f);
        rc.anchoredPosition = new Vector2(-14f, 0f);
        UnityEventTools.AddPersistentListener(cerrar.onClick, new UnityAction(ctrl.CerrarBanner));
    }

    // ----------------------------------------------------------------- Telemetría compacta + «Más detalles»
    private static void ConstruirTelemetria(RectTransform hud, SolarFieldController ctrl)
    {
        RectTransform card = Vidrio("Telemetria", hud, CVidrio, 22f, true, CBordeCyan, true, true);
        card.anchorMin = card.anchorMax = new Vector2(0f, 1f);
        card.pivot = new Vector2(0f, 1f);
        card.anchoredPosition = new Vector2(24f, -24f);
        card.sizeDelta = new Vector2(440f, 0f);
        LayoutVertical(card.gameObject, new RectOffset(26, 26, 20, 14), 2f, true);

        TextMeshProUGUI etPot = Texto(card, "EtiquetaPotencia", "POTENCIA GENERADA", 19f, CCyan, TextAlignmentOptions.MidlineLeft, Peso.Media, 4f, true);
        Altura(etPot.gameObject, 26f);

        ctrl.textoPotencia = Texto(card, "Potencia", "0.0 kW", 64f, CTexto, TextAlignmentOptions.MidlineLeft, Peso.Seminegrita, 0f, true);
        AutoTam(ctrl.textoPotencia, 32f, 64f);
        Altura(ctrl.textoPotencia.gameObject, 70f);

        Linea(card, 16f, CBordeNeutro);

        RectTransform filaEf = UI("Fila_Eficiencia", card);
        HorizontalLayoutGroup h = filaEf.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = true;
        h.spacing = 8f;
        Altura(filaEf.gameObject, 46f);
        TextMeshProUGUI etEf = Texto(filaEf, "Etiqueta", "EFICIENCIA", 19f, CTextoSec, TextAlignmentOptions.MidlineLeft, Peso.Media, 4f, true);
        etEf.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        ctrl.textoEficiencia = Texto(filaEf, "Valor", "0.0 %", 38f, CTexto, TextAlignmentOptions.MidlineRight, Peso.Seminegrita, 0f, true);
        AutoTam(ctrl.textoEficiencia, 22f, 38f);
        ctrl.textoEficiencia.gameObject.AddComponent<LayoutElement>().preferredWidth = 190f;

        // Barra de progreso fina (naranja)
        RectTransform pista = UI("BarraEficiencia", card);
        Altura(pista.gameObject, 8f);
        Image fondoBarra = pista.gameObject.AddComponent<Image>();
        fondoBarra.sprite = _sprPastilla; fondoBarra.type = Image.Type.Sliced;
        fondoBarra.color = new Color(1f, 1f, 1f, 0.14f); fondoBarra.raycastTarget = false;
        RectTransform relleno = UI("Relleno", pista);
        Estirar(relleno);
        Image imgRelleno = relleno.gameObject.AddComponent<Image>();
        imgRelleno.sprite = _sprPastilla;
        imgRelleno.type = Image.Type.Filled;
        imgRelleno.fillMethod = Image.FillMethod.Horizontal;
        imgRelleno.fillOrigin = (int)Image.OriginHorizontal.Left;
        imgRelleno.fillAmount = 0f;
        imgRelleno.color = CNaranja;
        imgRelleno.raycastTarget = false;
        ctrl.barraEficiencia = imgRelleno;
        ctrl.colorBarra = CNaranja;

        Separador(card, 8f);

        // Botón «Más detalles ▾»
        RectTransform tog = Vidrio("BtnDetalles", card, new Color32(255, 255, 255, 14), 12f, false, CBordeNeutro, false, true);
        Altura(tog.gameObject, 46f);
        Button bd = tog.gameObject.AddComponent<Button>();
        bd.targetGraphic = tog.Find("Fondo").GetComponent<Image>();
        ConfigurarBoton(bd);
        TextMeshProUGUI tt = Texto(tog, "Etiqueta", "Más detalles", 22f, CCyan, TextAlignmentOptions.MidlineLeft, Peso.Media, 1f, true);
        Estirar(tt.rectTransform);
        Margenes(tt.rectTransform, 16f, 0f, 60f, 0f);
        ctrl.textoBotonDetalles = tt;
        RectTransform ri = UI("Icono", tog);
        ri.anchorMin = ri.anchorMax = new Vector2(1f, 0.5f);
        ri.pivot = new Vector2(0.5f, 0.5f);
        ri.sizeDelta = new Vector2(26f, 26f);
        ri.anchoredPosition = new Vector2(-28f, 0f);
        Image icono = ri.gameObject.AddComponent<Image>();
        icono.sprite = _sprChevron; icono.color = CNaranja; icono.raycastTarget = false; icono.preserveAspect = true;
        ctrl.iconoDetalles = ri;
        UnityEventTools.AddPersistentListener(bd.onClick, new UnityAction(ctrl.AlternarDetalles));
        tog.gameObject.AddComponent<SPTButtonFeedback>();

        // Panel desplegable (hijo de la tarjeta, pegado debajo; ignora el layout)
        RectTransform det = Vidrio("Detalles", card, CVidrio, 22f, true, CBordeCyan, true, true);
        det.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        det.anchorMin = new Vector2(0f, 0f); det.anchorMax = new Vector2(1f, 0f);
        det.pivot = new Vector2(0.5f, 1f);
        det.sizeDelta = new Vector2(0f, 330f);
        det.anchoredPosition = new Vector2(0f, -12f);
        ctrl.grupoDetalles = det.gameObject.AddComponent<CanvasGroup>();
        ctrl.panelDetalles = det;

        RectTransform cont = UI("Contenido", det);
        Estirar(cont);
        LayoutVertical(cont.gameObject, new RectOffset(26, 26, 18, 14), 0f, false);
        ctrl.textoIrradiancia = FilaDato(cont, "Irradiancia", "Irradiancia en paneles");
        ctrl.textoIncidencia = FilaDato(cont, "Incidencia", "Ángulo de incidencia");
        ctrl.textoElevacion = FilaDato(cont, "Elevacion", "Elevación solar");
        ctrl.textoHogares = FilaDato(cont, "Hogares", "Hogares abastecidos");
        ctrl.textoCO2 = FilaDato(cont, "CO2", "CO2 evitado");
        Linea(cont, 16f, CBordeNeutro);
        ctrl.textoEstado = Texto(cont, "Estado", "• Generando", 22f, CTexto, TextAlignmentOptions.MidlineLeft, Peso.Normal, 0f, true);
        AutoTam(ctrl.textoEstado, 15f, 22f);
        Altura(ctrl.textoEstado.gameObject, 32f);
        RectTransform pie = UI("Pie", cont);
        HorizontalLayoutGroup hp = pie.gameObject.AddComponent<HorizontalLayoutGroup>();
        hp.childControlWidth = true; hp.childControlHeight = true; hp.childForceExpandWidth = true; hp.childForceExpandHeight = true;
        Altura(pie.gameObject, 26f);
        ctrl.textoCapacidad = Texto(pie, "Capacidad", "0 kWp", 19f, CTextoTenue, TextAlignmentOptions.MidlineLeft, Peso.Normal, 0f, true);
        ctrl.textoFPS = Texto(pie, "FPS", "60 FPS", 19f, CTextoTenue, TextAlignmentOptions.MidlineRight, Peso.Normal, 0f, true);
        det.gameObject.SetActive(false);
    }

    private static TextMeshProUGUI FilaDato(RectTransform padre, string nombre, string etiqueta)
    {
        RectTransform fila = UI("Fila_" + nombre, padre);
        HorizontalLayoutGroup h = fila.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = true;
        h.spacing = 10f;
        Altura(fila.gameObject, 44f);

        TextMeshProUGUI e = Texto(fila, "Etiqueta", etiqueta, 22f, CTextoSec, TextAlignmentOptions.MidlineLeft, Peso.Normal, 0f, true);
        AutoTam(e, 15f, 22f);
        e.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        TextMeshProUGUI val = Texto(fila, "Valor", "—", 26f, CTexto, TextAlignmentOptions.MidlineRight, Peso.Media, 0f, true);
        AutoTam(val, 16f, 26f);
        val.gameObject.AddComponent<LayoutElement>().preferredWidth = 150f;
        return val;
    }

    // ----------------------------------------------------------------- Riel lateral compacto (Vistas · Noche · Fichas · Menú)
    private static void ConstruirRielControles(RectTransform hud, SolarFieldController ctrl)
    {
        RectTransform riel = UI("RielControles", hud);
        riel.anchorMin = riel.anchorMax = new Vector2(1f, 1f);
        riel.pivot = new Vector2(1f, 1f);
        riel.anchoredPosition = new Vector2(-24f, -24f);
        riel.sizeDelta = new Vector2(84f, 0f);
        VerticalLayoutGroup v = LayoutVertical(riel.gameObject, new RectOffset(0, 0, 0, 0), 12f, true);
        v.childAlignment = TextAnchor.UpperRight;
        v.childForceExpandWidth = false;

        ctrl.botonesVista = new Button[3];
        for (int i = 0; i < 3; i++)
        {
            Button b = BotonVidrio(riel, "BtnVista" + (i + 1), (i + 1).ToString(), 40f, CVidrio, CTexto, Peso.Seminegrita, 20f, true, CBordeCyan, true);
            Tamano(b.gameObject, 84f, 84f);
            UnityEventTools.AddIntPersistentListener(b.onClick, new UnityAction<int>(ctrl.SetVista), i);
            ctrl.botonesVista[i] = b;
        }
        Separador(riel, 4f);

        Image icono;
        Button bNoche = BotonIcono(riel, "BtnDiaNoche", _sprLuna, 44f, CNaranja, CVidrio, 20f, out icono);
        Tamano(bNoche.gameObject, 84f, 84f);
        ctrl.imagenIconoNoche = icono;
        ctrl.textoBotonDiaNoche = null;
        UnityEventTools.AddPersistentListener(bNoche.onClick, new UnityAction(ctrl.AlternarDiaNoche));

        Button bFichas = BotonIcono(riel, "BtnFichas", _sprInfo, 44f, CNaranja, CVidrio, 20f, out icono);
        Tamano(bFichas.gameObject, 84f, 84f);
        UnityEventTools.AddPersistentListener(bFichas.onClick, new UnityAction(ctrl.AlternarFichas));

        Button bMenu = BotonIcono(riel, "BtnMenu", _sprMenu, 44f, CNaranja, CVidrio, 20f, out icono);
        Tamano(bMenu.gameObject, 84f, 84f);
        UnityEventTools.AddPersistentListener(bMenu.onClick, new UnityAction(ctrl.VolverAlMenu));
    }

    // ----------------------------------------------------------------- Barra de hora (reloj sin deformación)
    private static void ConstruirBarraHora(RectTransform hud, SolarFieldController ctrl)
    {
        RectTransform barra = Vidrio("BarraHora", hud, CVidrio, 22f, true, CBordeCyan, true, true);
        barra.anchorMin = new Vector2(0f, 0f);
        barra.anchorMax = new Vector2(1f, 0f);
        barra.pivot = new Vector2(0.5f, 0f);
        barra.offsetMin = new Vector2(24f, 24f);
        barra.offsetMax = new Vector2(-24f, 196f);

        // Fila superior izquierda: fase del día + nombre de la vista (nunca pasa de 58 % del ancho).
        RectTransform etiquetas = UI("Etiquetas", barra);
        etiquetas.anchorMin = new Vector2(0f, 1f); etiquetas.anchorMax = new Vector2(0.58f, 1f);
        etiquetas.pivot = new Vector2(0f, 1f);
        etiquetas.offsetMin = new Vector2(30f, -92f); etiquetas.offsetMax = new Vector2(0f, -18f);
        VerticalLayoutGroup vl = LayoutVertical(etiquetas.gameObject, new RectOffset(0, 0, 0, 0), 2f, false);
        vl.childAlignment = TextAnchor.MiddleLeft;

        ctrl.textoFaseDia = Texto(etiquetas, "FaseDia", "MAÑANA", 21f, CCyan, TextAlignmentOptions.MidlineLeft, Peso.Media, 3f, true);
        AutoTam(ctrl.textoFaseDia, 13f, 21f);
        Altura(ctrl.textoFaseDia.gameObject, 32f);
        ctrl.textoVistaActual = Texto(etiquetas, "VistaActual", SolarFieldController.NombresVista[0], 21f, CTextoSec, TextAlignmentOptions.MidlineLeft, Peso.Normal, 0f, true);
        AutoTam(ctrl.textoVistaActual, 13f, 21f);
        Altura(ctrl.textoVistaActual.gameObject, 32f);

        // Reloj: ancho fijo generoso, una sola línea, auto-tamaño y espacios no separables (sin saltos).
        ctrl.textoHora = Texto(barra, "Hora", "10:00 <size=46%>a. m.</size>", 54f, CTexto, TextAlignmentOptions.MidlineRight, Peso.Seminegrita, 0f, true);
        AutoTam(ctrl.textoHora, 30f, 54f);
        RectTransform rh = ctrl.textoHora.rectTransform;
        rh.anchorMin = rh.anchorMax = new Vector2(1f, 1f);
        rh.pivot = new Vector2(1f, 1f);
        rh.sizeDelta = new Vector2(340f, 80f);
        rh.anchoredPosition = new Vector2(-30f, -14f);

        // Fila inferior: [Auto]  6:00 ───slider─── 18:00
        RectTransform fila = UI("FilaSlider", barra);
        fila.anchorMin = new Vector2(0f, 0f); fila.anchorMax = new Vector2(1f, 0f);
        fila.pivot = new Vector2(0.5f, 0f);
        fila.offsetMin = new Vector2(26f, 16f); fila.offsetMax = new Vector2(-26f, 80f);
        HorizontalLayoutGroup h = fila.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 14f;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = false;

        Button auto = BotonVidrio(fila, "BtnAuto", "Auto", 24f, new Color32(255, 255, 255, 20), CTexto, Peso.Media, 16f, true, CBordeNeutro, false);
        Tamano(auto.gameObject, 118f, 52f);
        ctrl.textoBotonAuto = auto.GetComponentInChildren<TextMeshProUGUI>();
        UnityEventTools.AddPersistentListener(auto.onClick, new UnityAction(ctrl.AlternarAutoHora));

        TextMeshProUGUI t6 = Texto(fila, "Min", "6:00", 22f, CTextoSec, TextAlignmentOptions.Center, Peso.Normal, 0f, true);
        Tamano(t6.gameObject, 62f, 40f);

        Slider s = CrearSlider(fila);
        LayoutElement ls = s.gameObject.AddComponent<LayoutElement>();
        ls.flexibleWidth = 1f; ls.preferredHeight = 56f; ls.minHeight = 56f;
        s.minValue = 6f; s.maxValue = 18f; s.wholeNumbers = false; s.value = ctrl.hora;
        ctrl.sliderHora = s;
        UnityEventTools.AddPersistentListener(s.onValueChanged, new UnityAction<float>(ctrl.SetHora));

        TextMeshProUGUI t18 = Texto(fila, "Max", "18:00", 22f, CTextoSec, TextAlignmentOptions.Center, Peso.Normal, 0f, true);
        Tamano(t18.gameObject, 70f, 40f);
    }

    private static Slider CrearSlider(RectTransform padre)
    {
        const float asa = 40f;
        RectTransform raiz = UI("SliderHoraDelDia", padre);
        Image zonaToque = raiz.gameObject.AddComponent<Image>();   // zona táctil transparente de toda la altura
        zonaToque.color = new Color(1f, 1f, 1f, 0f);
        Slider s = raiz.gameObject.AddComponent<Slider>();

        RectTransform pista = UI("Pista", raiz);
        pista.anchorMin = new Vector2(0f, 0.5f); pista.anchorMax = new Vector2(1f, 0.5f);
        pista.offsetMin = new Vector2(asa * 0.5f, -3f); pista.offsetMax = new Vector2(-asa * 0.5f, 3f);
        Image ip = pista.gameObject.AddComponent<Image>();
        ip.sprite = _sprPastilla; ip.type = Image.Type.Sliced; ip.color = new Color(1f, 1f, 1f, 0.16f); ip.raycastTarget = false;

        RectTransform areaRelleno = UI("Fill Area", raiz);
        areaRelleno.anchorMin = new Vector2(0f, 0.5f); areaRelleno.anchorMax = new Vector2(1f, 0.5f);
        areaRelleno.offsetMin = new Vector2(asa * 0.5f, -3f); areaRelleno.offsetMax = new Vector2(-asa * 0.5f, 3f);
        RectTransform relleno = UI("Fill", areaRelleno);
        relleno.anchorMin = Vector2.zero; relleno.anchorMax = new Vector2(0f, 1f);
        relleno.sizeDelta = Vector2.zero;
        Image ir = relleno.gameObject.AddComponent<Image>();
        ir.sprite = _sprPastilla; ir.type = Image.Type.Sliced; ir.color = CNaranja; ir.raycastTarget = false;

        RectTransform areaManija = UI("Handle Slide Area", raiz);
        Estirar(areaManija);
        areaManija.offsetMin = new Vector2(asa * 0.5f, 0f); areaManija.offsetMax = new Vector2(-asa * 0.5f, 0f);
        RectTransform manija = UI("Handle", areaManija);
        manija.anchorMin = manija.anchorMax = new Vector2(0f, 0.5f);
        manija.pivot = new Vector2(0.5f, 0.5f);
        manija.sizeDelta = new Vector2(asa, asa);
        Image img = manija.gameObject.AddComponent<Image>();
        img.sprite = _sprCirculo; img.color = CBlanco; img.raycastTarget = false;
        RectTransform aro = UI("Aro", manija);
        Estirar(aro);
        Image iaro = aro.gameObject.AddComponent<Image>();
        iaro.sprite = _sprAroCirculo; iaro.color = CCyan; iaro.raycastTarget = false;

        s.fillRect = relleno;
        s.handleRect = manija;
        s.targetGraphic = img;
        s.direction = Slider.Direction.LeftToRight;
        Navigation nav = s.navigation; nav.mode = Navigation.Mode.None; s.navigation = nav;
        return s;
    }

    // ----------------------------------------------------------------- Joystick (Vista 3)
    private static void ConstruirJoystick(RectTransform hud, SolarFieldController ctrl)
    {
        RectTransform cont = UI("Joystick_Vista3", hud);
        // Abajo al centro, sobre la barra de hora: no choca con la telemetría ni con el riel en ninguna orientación.
        cont.anchorMin = cont.anchorMax = new Vector2(0.5f, 0f);
        cont.pivot = new Vector2(0.5f, 0f);
        cont.anchoredPosition = new Vector2(0f, 214f);
        cont.sizeDelta = new Vector2(300f, 330f);
        ctrl.contenedorJoystick = cont.gameObject;

        TextMeshProUGUI ayuda = Texto(cont, "Ayuda", "Joystick: avanzar  ·  Arrastrar pantalla: mirar", 20f, CTextoSec, TextAlignmentOptions.Center, Peso.Normal, 0f, true);
        RectTransform ra = ayuda.rectTransform;
        ra.anchorMin = new Vector2(0f, 1f); ra.anchorMax = new Vector2(1f, 1f);
        ra.pivot = new Vector2(0.5f, 1f);
        ra.offsetMin = new Vector2(-120f, -44f); ra.offsetMax = new Vector2(120f, 0f);

        RectTransform fondo = UI("Fondo", cont);
        fondo.anchorMin = fondo.anchorMax = new Vector2(0.5f, 0f);
        fondo.pivot = new Vector2(0.5f, 0f);
        fondo.sizeDelta = new Vector2(250f, 250f);
        fondo.anchoredPosition = Vector2.zero;
        Image ifondo = fondo.gameObject.AddComponent<Image>();
        ifondo.sprite = _sprCirculo;
        ifondo.color = new Color(0.03f, 0.05f, 0.08f, 0.55f);
        RectTransform aro = UI("Aro", fondo);
        Estirar(aro);
        Image iaro = aro.gameObject.AddComponent<Image>();
        iaro.sprite = _sprAroCirculo; iaro.color = new Color(CCyan.r, CCyan.g, CCyan.b, 0.45f); iaro.raycastTarget = false;

        RectTransform perilla = UI("Perilla", fondo);
        perilla.anchorMin = perilla.anchorMax = new Vector2(0.5f, 0.5f);
        perilla.sizeDelta = new Vector2(104f, 104f);
        Image iper = perilla.gameObject.AddComponent<Image>();
        iper.sprite = _sprCirculo;
        iper.color = new Color(CCyan.r, CCyan.g, CCyan.b, 0.9f);
        iper.raycastTarget = false;

        SPTVirtualJoystick joy = fondo.gameObject.AddComponent<SPTVirtualJoystick>();
        joy.fondo = fondo;
        joy.perilla = perilla;
        ctrl.joystick = joy;
        cont.gameObject.SetActive(false);
    }

    // ----------------------------------------------------------------- Modales (Fichas / Acerca de)
    private static RectTransform Modal(string nombre, RectTransform padre, out RectTransform contenido)
    {
        RectTransform velo = UI(nombre, padre);
        Estirar(velo);
        Image iv = velo.gameObject.AddComponent<Image>();
        iv.color = CVelo; // bloquea los toques al 3D mientras está abierto

        RectTransform card = Vidrio("Tarjeta", velo, CVidrioFuerte, 26f, true, CBordeCyan, true, true);
        card.anchorMin = new Vector2(0.5f, 0f);
        card.anchorMax = new Vector2(0.5f, 1f);
        card.pivot = new Vector2(0.5f, 0.5f);
        card.sizeDelta = new Vector2(980f, 0f);
        card.offsetMin = new Vector2(-490f, 100f);
        card.offsetMax = new Vector2(490f, -100f);
        LayoutVertical(card.gameObject, new RectOffset(40, 40, 30, 34), 18f, false);
        contenido = card;
        return velo;
    }

    private static RectTransform Cabecera(RectTransform card, string titulo, UnityAction alCerrar)
    {
        RectTransform cab = UI("Cabecera", card);
        Altura(cab.gameObject, 72f);
        TextMeshProUGUI t = Texto(cab, "Titulo", titulo, 26f, CCyan, TextAlignmentOptions.MidlineLeft, Peso.Media, 5f, true);
        Estirar(t.rectTransform);
        t.rectTransform.offsetMax = new Vector2(-90f, 0f);

        Button cerrar = BotonVidrio(cab, "Cerrar", "×", 40f, new Color32(255, 255, 255, 18), CTexto, Peso.Media, 16f, true, CBordeNeutro, false);
        RectTransform rc = (RectTransform)cerrar.transform;
        rc.anchorMin = rc.anchorMax = new Vector2(1f, 0.5f);
        rc.pivot = new Vector2(1f, 0.5f);
        rc.sizeDelta = new Vector2(68f, 68f);
        rc.anchoredPosition = Vector2.zero;
        UnityEventTools.AddPersistentListener(cerrar.onClick, alCerrar);
        return cab;
    }

    private static ScrollRect Desplazable(RectTransform card, string nombre, string contenido, out TextMeshProUGUI cuerpo)
    {
        RectTransform vista = UI(nombre, card);
        LayoutElement le = vista.gameObject.AddComponent<LayoutElement>();
        le.flexibleHeight = 1f;
        le.minHeight = 200f;
        Image iv = vista.gameObject.AddComponent<Image>();
        iv.color = new Color(1f, 1f, 1f, 0.04f);
        iv.sprite = _sprVidrio;
        iv.type = Image.Type.Sliced;
        iv.pixelsPerUnitMultiplier = 1.6f;
        vista.gameObject.AddComponent<RectMask2D>();
        ScrollRect sr = vista.gameObject.AddComponent<ScrollRect>();

        RectTransform cont = UI("Texto", vista);
        cont.anchorMin = new Vector2(0f, 1f);
        cont.anchorMax = new Vector2(1f, 1f);
        cont.pivot = new Vector2(0.5f, 1f);
        cont.offsetMin = Vector2.zero; cont.offsetMax = Vector2.zero;
        LayoutVertical(cont.gameObject, new RectOffset(26, 26, 20, 30), 0f, true);

        cuerpo = Texto(cont, "Cuerpo", contenido, 28f, new Color(CTexto.r, CTexto.g, CTexto.b, 0.94f), TextAlignmentOptions.TopLeft, Peso.Normal);
        cuerpo.lineSpacing = 6f;
        cuerpo.paragraphSpacing = 10f;

        sr.content = cont;
        sr.viewport = vista;
        sr.horizontal = false;
        sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Elastic;
        sr.scrollSensitivity = 40f;
        return sr;
    }

    private static void ConstruirFichas(RectTransform segura, SolarFieldController ctrl)
    {
        RectTransform card;
        RectTransform modal = Modal("FichasTecnicas", segura, out card);
        ctrl.panelFichas = modal.gameObject;

        Cabecera(card, "FICHAS TÉCNICAS", new UnityAction(ctrl.CerrarFichas));

        RectTransform pestanas = UI("Pestanas", card);
        HorizontalLayoutGroup h = pestanas.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 12f;
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = true; h.childForceExpandHeight = true;
        Altura(pestanas.gameObject, 72f);
        string[] nombres = { "Celdas", "Estructura", "LOD & URP" };
        ctrl.botonesFicha = new Button[3];
        for (int i = 0; i < 3; i++)
        {
            Button b = BotonVidrio(pestanas, "Tab" + (i + 1), nombres[i], 27f, ctrl.colorBotonNormal, CTexto, Peso.Media, 16f, true, CBordeCyan, false);
            UnityEventTools.AddIntPersistentListener(b.onClick, new UnityAction<int>(ctrl.MostrarFicha), i);
            ctrl.botonesFicha[i] = b;
        }

        ctrl.textoFichaTitulo = Texto(card, "TituloFicha", SolarFieldController.FichasTitulosPorDefecto[0], 38f, CTexto, TextAlignmentOptions.MidlineLeft, Peso.Seminegrita, 0f, true);
        AutoTam(ctrl.textoFichaTitulo, 24f, 38f);
        Altura(ctrl.textoFichaTitulo.gameObject, 56f);

        TextMeshProUGUI cuerpo;
        ctrl.scrollFicha = Desplazable(card, "Contenido", SolarFieldController.FichasCuerposPorDefecto[0], out cuerpo);
        ctrl.textoFichaCuerpo = cuerpo;

        modal.gameObject.SetActive(false);
    }

    // ----------------------------------------------------------------- Menú principal (diseño plano moderno)
    private static void ConstruirMenu(RectTransform segura, SolarFieldController ctrl)
    {
        RectTransform menu = UI("MenuInicio", segura);
        Estirar(menu);
        ctrl.panelMenu = menu.gameObject;

        // Velo plano + degradado inferior (legibilidad de los créditos)
        Image velo = Imagen(menu, "Velo", null, new Color32(4, 8, 13, 118), Image.Type.Simple, true);
        Image grad = Imagen(menu, "Degradado", _sprDegradado, new Color32(3, 6, 10, 215), Image.Type.Simple, false);
        grad.rectTransform.anchorMin = new Vector2(0f, 0f); grad.rectTransform.anchorMax = new Vector2(1f, 0.30f);
        grad.rectTransform.offsetMin = Vector2.zero; grad.rectTransform.offsetMax = Vector2.zero;

        RectTransform tarjeta = Vidrio("Tarjeta", menu, new Color32(8, 11, 16, 186), 30f, true, CBordeCyan, true, true);
        tarjeta.anchorMin = tarjeta.anchorMax = new Vector2(0.5f, 0.5f);
        tarjeta.pivot = new Vector2(0.5f, 0.5f);
        tarjeta.anchoredPosition = new Vector2(0f, 52f);
        tarjeta.sizeDelta = new Vector2(860f, 0f);
        VerticalLayoutGroup v = LayoutVertical(tarjeta.gameObject, new RectOffset(56, 56, 48, 40), 12f, true);
        v.childAlignment = TextAnchor.UpperCenter;

        TextMeshProUGUI inst = Texto(tarjeta, "Institucion", "UNIVERSIDAD MARISTA DE MÉRIDA", 23f, CCyan, TextAlignmentOptions.Center, Peso.Media, 7f, true);
        AutoTam(inst, 14f, 23f);
        Altura(inst.gameObject, 32f);

        TextMeshProUGUI titulo = Texto(tarjeta, "Titulo", "<color=#00D1FF>Sol</color> Para Todos", 116f, CTexto, TextAlignmentOptions.Center, Peso.Negrita, -1f, true);
        AutoTam(titulo, 56f, 116f);
        Altura(titulo.gameObject, 128f);

        TextMeshProUGUI sub = Texto(tarjeta, "Subtitulo", "Simulador interactivo de energía solar fotovoltaica", 31f, CTextoSec, TextAlignmentOptions.Center, Peso.Normal, 0f, true);
        AutoTam(sub, 18f, 31f);
        Altura(sub.gameObject, 44f);

        RectTransform lineaCont = UI("Linea", tarjeta);
        Altura(lineaCont.gameObject, 12f);
        RectTransform linea = UI("Trazo", lineaCont);
        linea.anchorMin = linea.anchorMax = new Vector2(0.5f, 0.5f);
        linea.sizeDelta = new Vector2(96f, 3f);
        Image il = linea.gameObject.AddComponent<Image>();
        il.color = CCyan; il.raycastTarget = false;

        // Frases de impacto rotativas
        RectTransform caja = UI("FrasesImpacto", tarjeta);
        Altura(caja.gameObject, 104f);
        CanvasGroup cg = caja.gameObject.AddComponent<CanvasGroup>();
        cg.interactable = false; cg.blocksRaycasts = false;
        TextMeshProUGUI frase = Texto(caja, "Frase", Frases[0], 29f, new Color(CTexto.r, CTexto.g, CTexto.b, 0.92f), TextAlignmentOptions.Center, Peso.Ligera);
        Estirar(frase.rectTransform);
        Margenes(frase.rectTransform, 24f, 0f, 24f, 0f);
        AutoTam(frase, 20f, 29f);
        SPTPhraseRotator rot = caja.gameObject.AddComponent<SPTPhraseRotator>();
        rot.texto = frase;
        rot.grupo = cg;
        rot.frases = (string[])Frases.Clone();

        Separador(tarjeta, 6f);

        // Botón primario: blanco sólido, texto oscuro, sombra suave
        Button iniciar = BotonVidrio(tarjeta, "BtnIniciar", "Iniciar simulación", 38f, CBlanco, CTextoOscuro, Peso.Seminegrita, 20f, false, CBordeNeutro, true);
        CentrarBoton(iniciar, 640f, 100f);
        UnityEventTools.AddPersistentListener(iniciar.onClick, new UnityAction(ctrl.IniciarSimulacion));

        // Secundario: contorno fino
        Button acerca = BotonVidrio(tarjeta, "BtnAcerca", "Acerca del proyecto", 32f, new Color32(255, 255, 255, 12), CTexto, Peso.Media, 20f, true, new Color32(255, 255, 255, 96), false);
        CentrarBoton(acerca, 640f, 84f);
        UnityEventTools.AddPersistentListener(acerca.onClick, new UnityAction(ctrl.MostrarAcerca));

        // Terciario: solo texto
        Button salir = BotonVidrio(tarjeta, "BtnSalir", "Salir", 28f, new Color32(255, 255, 255, 0), CTextoSec, Peso.Normal, 16f, false, CBordeNeutro, false);
        CentrarBoton(salir, 640f, 60f);
        UnityEventTools.AddPersistentListener(salir.onClick, new UnityAction(ctrl.Salir));

        TextMeshProUGUI creditos = Texto(menu, "Creditos",
            "Emiliano Gasque  ·  Ingeniería en Tecnologías Digitales\nGráficas por Computadora  ·  Prof. Luis de Jesús López Hernández",
            21f, CTextoSec, TextAlignmentOptions.Bottom, Peso.Normal);
        RectTransform rc = creditos.rectTransform;
        rc.anchorMin = new Vector2(0f, 0f); rc.anchorMax = new Vector2(1f, 0f);
        rc.pivot = new Vector2(0.5f, 0f);
        rc.offsetMin = new Vector2(30f, 24f); rc.offsetMax = new Vector2(-30f, 96f);

        // Logo de la Universidad Marista: silueta blanca con volumen (extrusión + sombra), esquina inferior izquierda.
        Sprite logo = CargarLogoMarista();
        if (logo != null)
        {
            RectTransform rl = UI("LogoMarista", menu);
            rl.anchorMin = rl.anchorMax = new Vector2(0f, 0f);
            rl.pivot = new Vector2(0f, 0f);
            rl.anchoredPosition = new Vector2(26f, 18f);
            rl.sizeDelta = new Vector2(112f, 158f);
            Image il2 = rl.gameObject.AddComponent<Image>();
            il2.sprite = logo;
            il2.preserveAspect = true;
            il2.raycastTarget = false;
        }
    }

    private static string _estadoLogo = "Logo: no se intentó.";

    private static Sprite CargarLogoMarista()
    {
        const string ruta = "Assets/SolParaTodos/UI/Logo_Marista_Blanco.png";
        try
        {
            AssetDatabase.ImportAsset(ruta, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter ti = AssetImporter.GetAtPath(ruta) as TextureImporter;
            if (ti == null) { _estadoLogo = "Logo: NO se encontró " + ruta + " en el proyecto (cópialo ahí y reconstruye)."; return null; }
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.filterMode = FilterMode.Bilinear;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
            Sprite spr = AssetDatabase.LoadAssetAtPath<Sprite>(ruta);
            _estadoLogo = spr != null ? "Logo de la Marista añadido al menú." : "Logo: se importó pero no se pudo cargar como Sprite.";
            return spr;
        }
        catch (Exception e)
        {
            _estadoLogo = "Logo: error — " + e.Message;
            return null;
        }
    }

    private static void CentrarBoton(Button b, float ancho, float alto)
    {
        // Centra un botón de ancho fijo dentro de una columna que fuerza el ancho de sus hijos.
        RectTransform envoltura = UI(b.name + "_Fila", b.transform.parent);
        envoltura.SetSiblingIndex(b.transform.GetSiblingIndex());
        Altura(envoltura.gameObject, alto);
        b.transform.SetParent(envoltura, false);
        RectTransform rt = (RectTransform)b.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(ancho, alto);
        rt.anchoredPosition = Vector2.zero;
    }

    private static void ConstruirAcerca(RectTransform segura, SolarFieldController ctrl)
    {
        RectTransform card;
        RectTransform modal = Modal("AcercaDelProyecto", segura, out card);
        ctrl.panelAcerca = modal.gameObject;

        Cabecera(card, "ACERCA DEL PROYECTO", new UnityAction(ctrl.CerrarAcerca));

        TextMeshProUGUI cuerpo;
        Desplazable(card, "Contenido", TextoAcerca, out cuerpo);

        Button volver = BotonVidrio(card, "BtnVolver", "Volver", 30f, CBlanco, CTextoOscuro, Peso.Seminegrita, 18f, false, CBordeNeutro, false);
        Altura(volver.gameObject, 80f);
        UnityEventTools.AddPersistentListener(volver.onClick, new UnityAction(ctrl.CerrarAcerca));

        modal.gameObject.SetActive(false);
    }

    // =================================================================== Textos
    // =================================================================== Textos
    private static readonly string[] Frases =
    {
        "En aproximadamente una hora, la Tierra recibe del sol más energía de la que la humanidad consume en un año.",
        "Democratizar la energía es acercar el sol a cada hogar, escuela y comunidad.",
        "La transición energética empieza hoy: cada panel instalado es energía limpia para mañana.",
        "Un ángulo bien calculado convierte más radiación en electricidad con el mismo panel.",
        "Yucatán recibe sol intenso casi todo el año: el recurso ya está aquí, falta captarlo.",
        "Energía limpia, local y accesible: el sol no manda factura."
    };

    private const string TextoAcerca =
        "<b><color=#00D1FF>Sol Para Todos</color></b> es una aplicación interactiva para concientizar sobre la accesibilidad y el potencial de la energía solar fotovoltaica. Muestra de forma visual cómo la inclinación de los paneles, la trayectoria del sol y la optimización geométrica hacen viable la generación de energía limpia a gran escala.\n\n" +
        "<b>¿Qué puedes hacer?</b>\n" +
        "• Mover la <b>hora del día</b> (6:00–18:00) y ver cómo cambian las sombras, la eficiencia y la potencia.\n" +
        "• Observar la <b>telemetría</b>: eficiencia por producto punto entre el sol y la normal de los paneles, irradiancia, kW, hogares abastecidos y CO2 evitado.\n" +
        "• Alternar entre <b>modo día</b> y el <b>Modo Crepúsculo / Noche de Laboratorio</b>, con indicadores luminosos que resaltan la silueta del parque.\n" +
        "• Cambiar entre <b>3 vistas</b>: panorámica del parque, detalle de un panel y recorrido por la estructura con joystick.\n" +
        "• Consultar las <b>fichas técnicas</b> de celdas, estructura y optimización gráfica.\n\n" +
        "<b>Tecnología</b>\n" +
        "Unity 6 · Universal Render Pipeline (Mobile) · Android ARM64 / IL2CPP · LOD Group de 3 niveles · SRP Batcher · MaterialPropertyBlock · MSAA 4x · Input System.\n\n" +
        "<b>Créditos</b>\n" +
        "Autor: Emiliano Gasque\n" +
        "Ingeniería en Tecnologías Digitales — Universidad Marista de Mérida\n" +
        "Asignatura: Gráficas por Computadora\n" +
        "Profesor: Luis de Jesús López Hernández\n\n" +
        "<size=85%><color=#A2B6CC>Las cifras de potencia, hogares y CO2 son estimaciones didácticas (módulo de 550 Wp, PR 0.80, consumo de 0.35 kW por hogar y 0.438 kg CO2/kWh); no sustituyen un estudio de ingeniería.</color></size>";

    // =================================================================== Utilidades de menú
    [MenuItem(Menu + "Verificar SRP Batcher, Materiales y LODs", false, 20)]
    public static void Verificar()
    {
        var sb = new System.Text.StringBuilder();
        RenderPipelineAsset rpa = QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline;
        UniversalRenderPipelineAsset urp = rpa as UniversalRenderPipelineAsset;
        if (urp != null)
        {
            sb.AppendLine("Pipeline activo: " + urp.name);
            sb.AppendLine("  SRP Batcher: " + (urp.useSRPBatcher ? "ACTIVO" : "desactivado"));
            sb.AppendLine("  MSAA: " + urp.msaaSampleCount + "x · Render Scale: " + urp.renderScale.ToString("0.00"));
            PropertyInfo crossFade = typeof(UniversalRenderPipelineAsset).GetProperty("enableLODCrossFade");
            if (crossFade != null) sb.AppendLine("  LOD Cross Fade: " + crossFade.GetValue(urp, null));
        }
        else sb.AppendLine("No se detectó un UniversalRenderPipelineAsset activo.");

        Transform[] filas = BuscarFilas();
        int grupos = 0, tresNiveles = 0;
        var usoMateriales = new Dictionary<Material, int>();
        foreach (Transform f in filas)
        {
            foreach (LODGroup g in f.GetComponentsInChildren<LODGroup>(true))
            {
                grupos++;
                if (g.lodCount == 3) tresNiveles++;
            }
            foreach (Renderer r in f.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    int c; usoMateriales.TryGetValue(m, out c);
                    usoMateriales[m] = c + 1;
                }
        }
        sb.AppendLine("\nFilas: " + filas.Length + " · LOD Groups: " + grupos + " (con 3 niveles: " + tresNiveles + ")");
        sb.AppendLine("Materiales distintos en los paneles: " + usoMateriales.Count);
        foreach (KeyValuePair<Material, int> kv in usoMateriales.OrderByDescending(k => k.Value))
            sb.AppendLine("  • " + kv.Key.name + " — " + kv.Value + " usos · " + kv.Key.shader.name +
                          (kv.Key.IsKeywordEnabled("_EMISSION") ? " · _EMISSION" : ""));
        var duplicados = usoMateriales.Keys.GroupBy(m => m.name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        sb.AppendLine(duplicados.Count == 0
            ? "Sin materiales duplicados: todas las instancias comparten material (ideal para SRP Batcher)."
            : "Materiales duplicados por nombre: " + string.Join(", ", duplicados) + " → usa 'Unificar Materiales Duplicados'.");

        SolarFieldController ctrl = Todos<SolarFieldController>().FirstOrDefault();
        sb.AppendLine(ctrl != null ? "\nSolarFieldController presente en '" + ctrl.name + "'." : "\nFalta SolarFieldController: ejecuta 'Construir Escena Completa y UI'.");

        Debug.Log("<b>[Sol Para Todos] Verificación</b>\n" + sb);
        EditorUtility.DisplayDialog("Sol Para Todos — Verificación", sb.ToString(), "OK");
    }

    [MenuItem(Menu + "Unificar Materiales Duplicados de Paneles", false, 21)]
    public static void UnificarMateriales()
    {
        Transform[] filas = BuscarFilas();
        var renderers = filas.SelectMany(f => f.GetComponentsInChildren<Renderer>(true)).ToList();
        var uso = new Dictionary<Material, int>();
        foreach (Renderer r in renderers)
            foreach (Material m in r.sharedMaterials)
                if (m != null) { int c; uso.TryGetValue(m, out c); uso[m] = c + 1; }

        var canonico = uso.Keys.GroupBy(m => m.name)
                          .ToDictionary(g => g.Key, g => g.OrderByDescending(m => uso[m]).First());
        int cambios = 0;
        Undo.IncrementCurrentGroup();
        foreach (Renderer r in renderers)
        {
            Material[] mats = r.sharedMaterials;
            bool cambio = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                Material c = canonico[mats[i].name];
                if (c != mats[i]) { mats[i] = c; cambio = true; }
            }
            if (cambio)
            {
                Undo.RecordObject(r, "Unificar materiales");
                r.sharedMaterials = mats;
                cambios++;
            }
        }
        if (cambios > 0) EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorUtility.DisplayDialog("Sol Para Todos", cambios == 0
            ? "Todos los paneles ya comparten los mismos materiales. No hubo cambios."
            : "Se unificaron materiales en " + cambios + " renderers. Guarda la escena.", "OK");
    }

    // ----------------------------------------------------------------- Tamaño de la granja
    private static void EstablecerPreset(int i)
    {
        EditorPrefs.SetInt(ClavePreset, i);
        EditorUtility.DisplayDialog("Sol Para Todos", "Tamaño de granja: " + Presets[i].nombre +
            ".\n\nEjecuta «Construir Escena Completa y UI» para reorganizar los paneles (se clonan o eliminan unidades por código).", "OK");
    }

    private static bool MarcarPreset(string ruta, int i)
    {
        UnityEditor.Menu.SetChecked(ruta, EditorPrefs.GetInt(ClavePreset, 1) == i);
        return true;
    }

    [MenuItem(Menu + "Tamaño de la granja/Compacta (192 unidades)", false, 10)] private static void Preset0() { EstablecerPreset(0); }
    [MenuItem(Menu + "Tamaño de la granja/Compacta (192 unidades)", true)] private static bool Preset0V() { return MarcarPreset(Menu + "Tamaño de la granja/Compacta (192 unidades)", 0); }
    [MenuItem(Menu + "Tamaño de la granja/Estándar (308 unidades)", false, 11)] private static void Preset1() { EstablecerPreset(1); }
    [MenuItem(Menu + "Tamaño de la granja/Estándar (308 unidades)", true)] private static bool Preset1V() { return MarcarPreset(Menu + "Tamaño de la granja/Estándar (308 unidades)", 1); }
    [MenuItem(Menu + "Tamaño de la granja/Grande (480 unidades)", false, 12)] private static void Preset2() { EstablecerPreset(2); }
    [MenuItem(Menu + "Tamaño de la granja/Grande (480 unidades)", true)] private static bool Preset2V() { return MarcarPreset(Menu + "Tamaño de la granja/Grande (480 unidades)", 2); }

    [MenuItem(Menu + "Restaurar sombras URP originales", false, 41)]
    public static void RestaurarSombrasURP()
    {
        if (!File.Exists(RutaRespaldoURP))
        {
            EditorUtility.DisplayDialog("Sol Para Todos", "No hay respaldo: la distancia de sombras nunca se modificó.", "OK");
            return;
        }
        string[] partes = File.ReadAllText(RutaRespaldoURP).Split('|');
        float valor;
        RenderPipelineAsset rpa = QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline;
        UniversalRenderPipelineAsset urp = rpa as UniversalRenderPipelineAsset;
        if (urp == null || partes.Length < 2 || !float.TryParse(partes[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out valor)) return;
        urp.shadowDistance = valor;
        EditorUtility.SetDirty(urp);
        AssetDatabase.SaveAssetIfDirty(urp);
        AssetDatabase.DeleteAsset(RutaRespaldoURP);
        EditorUtility.DisplayDialog("Sol Para Todos", "Distancia de sombras restaurada a " + valor.ToString("0") + " m en '" + urp.name + "'.", "OK");
    }

    [MenuItem(Menu + "Eliminar Objetos Generados", false, 40)]
    public static void EliminarTodo()
    {
        if (!EditorUtility.DisplayDialog("Sol Para Todos", "¿Eliminar UI, controlador, vistas, inversores, indicadores, calles y volumen nocturno generados?\n(Los paneles reordenados y el terreno se conservan. Puedes deshacer con Ctrl+Z.)", "Eliminar", "Cancelar")) return;
        Undo.IncrementCurrentGroup();
        int n = EliminarGenerados();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[Sol Para Todos] Objetos generados eliminados: " + n);
    }

    [MenuItem(Menu + "Abrir Guía Rápida", false, 60)]
    public static void AbrirGuia()
    {
        Object guia = AssetDatabase.LoadAssetAtPath<Object>(RutaGuia);
        if (guia != null) AssetDatabase.OpenAsset(guia);
        else EditorUtility.DisplayDialog("Sol Para Todos", "No se encontró " + RutaGuia, "OK");
    }
}
