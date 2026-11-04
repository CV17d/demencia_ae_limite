// ============================================================================
// RuntimeDebugOverlay.cs — High-Legibility Horror Visor HUD (Halo Spatial Layout)
// ============================================================================
// Minimalist, high-contrast HUD designed for psychological horror and suspense:
//   - Drop-shadow text rendering (Black +2px offset) for instant readability on dark scenes
//   - Bone-white vibrant text [new Color(0.9f, 0.9f, 0.85f)]
//   - Full-length semi-transparent black track (0,0,0,0.7) for maximum bar visibility
//   - 14px tall tactile progress bars & 2px medium gray divider lines
//   - 20% brightened Crimson Health & Withered Purple Sanity fills
//   - Isolated bottom-right digital ammo readout [ AMMO // [ 10 / 12 ] ]
// ============================================================================

using UnityEngine;
using DemenciaNoLimite.Core;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Core.Events;

namespace DemenciaNoLimite.UI
{
    public class RuntimeDebugOverlay : MonoBehaviour
    {
        // --- PALETA DE TERROR Y ALTA LEGIBILIDAD ---
        private static readonly Color ColorBoneText        = new Color(0.9f, 0.9f, 0.85f, 1.0f);
        private static readonly Color ColorDividerLine     = new Color(0.5f, 0.5f, 0.5f, 0.7f);
        private static readonly Color ColorBarTrack        = new Color(0.0f, 0.0f, 0.0f, 0.7f);
        private static readonly Color ColorBloodRed        = new Color(0.7f, 0.15f, 0.15f, 1.0f);
        private static readonly Color ColorWitheredPurple  = new Color(0.5f, 0.35f, 0.7f, 1.0f);
        private static readonly Color ColorTrackBorder     = new Color(0.4f, 0.4f, 0.4f, 0.5f);
        private static readonly Color ColorTerminalDim     = new Color(0.6f, 0.6f, 0.58f, 0.8f);

        private HUDController _hud;
        private ElevatorUIController _elevatorUI;
        private BossHealthBarUI _bossUI;

        private Texture2D _whiteTex;
        private GUIStyle _sectorStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _valStyle;
        private GUIStyle _ammoLargeStyle;
        private GUIStyle _tokenStyle;
        private GUIStyle _terminalStyle;
        private GUIStyle _bossHeaderStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _cardStyle;

        private void Awake()
        {
            _hud = GetComponent<HUDController>();
            if (_hud == null) _hud = FindAnyObjectByType<HUDController>();

            _elevatorUI = GetComponent<ElevatorUIController>();
            if (_elevatorUI == null) _elevatorUI = FindAnyObjectByType<ElevatorUIController>();

            _bossUI = GetComponent<BossHealthBarUI>();
            if (_bossUI == null) _bossUI = FindAnyObjectByType<BossHealthBarUI>();

            _whiteTex = new Texture2D(1, 1);
            _whiteTex.SetPixel(0, 0, Color.white);
            _whiteTex.Apply();
        }

        private void InitStyles()
        {
            if (_sectorStyle != null) return;

            _sectorStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };

            _valStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight
            };

            _ammoLargeStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight
            };

            _tokenStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight
            };

            _terminalStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleLeft
            };

            _bossHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold
            };

            _cardStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 12,
                wordWrap = true,
                padding = new RectOffset(10, 10, 10, 10)
            };
        }

        private void OnGUI()
        {
            InitStyles();

            // 1. Crosshair táctico tenue
            DrawCrosshair();

            // 2. HUD Descentralizado de Alta Legibilidad
            DrawHUD();

            // 3. Barra de Encuentro con Jefe
            if (_bossUI != null && _bossUI.IsVisible)
            {
                DrawBossBar();
            }

            // 4. Diálogo de Elevador Fronterizo
            if (_elevatorUI != null && _elevatorUI.IsElevatorUIVisible)
            {
                DrawElevatorDialog();
            }
        }

        private void Update()
        {
            // Tab para liberar cursor durante diálogos o inspección
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                Cursor.lockState = Cursor.lockState == CursorLockMode.Locked ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = Cursor.lockState == CursorLockMode.None;
            }
        }

        // ====================================================================
        // SOMBREADO DE TEXTO (CRÍTICO: +2px OFFSET EN NEGRO PURO)
        // ====================================================================
        private void DrawTextWithShadow(Rect rect, string text, GUIStyle style, Color mainColor)
        {
            if (string.IsNullOrEmpty(text)) return;

            // 1. Sombra: negro puro con offset (+2px en X e Y)
            style.normal.textColor = Color.black;
            GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, style);

            // 2. Texto principal en la capa superior
            style.normal.textColor = mainColor;
            GUI.Label(rect, text, style);
        }

        // ====================================================================
        // RETÍCULA DE VISOR
        // ====================================================================
        private void DrawCrosshair()
        {
            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            Color reticleColor = new Color(0.7f, 0.7f, 0.65f, 0.5f);

            // Ticks sutiles del visor
            DrawRect(new Rect(cx - 8, cy - 1, 5, 2), reticleColor);
            DrawRect(new Rect(cx + 3, cy - 1, 5, 2), reticleColor);
            DrawRect(new Rect(cx - 1, cy - 8, 2, 5), reticleColor);
            DrawRect(new Rect(cx - 1, cy + 3, 2, 5), reticleColor);

            // Micro punto central
            DrawRect(new Rect(cx - 0.5f, cy - 0.5f, 1, 1), Color.white);
        }

        // ====================================================================
        // HUD PRINCIPAL (ESQUINAS DESCENTRALIZADAS & ALTA VISIBILIDAD)
        // ====================================================================
        private void DrawHUD()
        {
            // ----------------------------------------------------------------
            // ESQUINA SUPERIOR IZQUIERDA: VITALES Y CORDURA (FLOTANTE)
            // ----------------------------------------------------------------
            float lx = 32f;
            float ly = 26f;
            float barW = 210f;
            float barH = 14f; // 14px de altura para alta visibilidad

            // Identificador de Sector / Piso
            string floorText = _hud != null ? _hud.FloorDisplayText : "TORRE FRONTERA";
            DrawTextWithShadow(new Rect(lx, ly, barW + 80f, 20f), $"SEC // {floorText.ToUpper()}", _sectorStyle, ColorBoneText);
            ly += 22f;

            // Línea divisoria horizontal de 2px de grosor
            DrawRect(new Rect(lx, ly, barW, 2f), ColorDividerLine);
            ly += 12f;

            // Salud del Sujeto (Rojo Aclarado 20% con Sombra)
            float hpPct = _hud != null ? _hud.HealthNormalized : 1f;
            string hpText = _hud != null ? _hud.HealthDisplayText : "100 / 100";
            DrawTextWithShadow(new Rect(lx, ly, 100f, 18f), "SALUD //", _labelStyle, ColorBoneText);
            DrawTextWithShadow(new Rect(lx + 100f, ly, barW - 100f, 18f), hpText, _valStyle, ColorBoneText);
            ly += 20f;
            DrawProgressBar(lx, ly, barW, barH, hpPct, ColorBloodRed);
            ly += barH + 12f;

            // Cordura / Degradación Mental (Morado Frío Aclarado 20%)
            float sanityPct = _hud != null ? _hud.SanityNormalized : 0f;
            string tierText = _hud != null ? _hud.SanityTierDisplayText : "STABLE";
            string sanityDisplay = $"{tierText} [{(sanityPct * 100f):F0}%]";
            DrawTextWithShadow(new Rect(lx, ly, 100f, 18f), "CORDURA //", _labelStyle, ColorBoneText);
            DrawTextWithShadow(new Rect(lx + 100f, ly, barW - 100f, 18f), sanityDisplay, _valStyle, ColorBoneText);
            ly += 20f;
            DrawProgressBar(lx, ly, barW, barH, 1f - sanityPct, ColorWitheredPurple);

            // ----------------------------------------------------------------
            // ESQUINA INFERIOR DERECHA: MUNICIÓN Y FICHAS AISLADAS (ESTILO HALO)
            // ----------------------------------------------------------------
            float panelW = 280f;
            float rx = Screen.width - panelW - 32f;
            float ry = Screen.height - 30f;

            string rawAmmo = _hud != null ? _hud.AmmoDisplayText : "-- / --";
            string formattedAmmo = FormatAmmoHalo(rawAmmo);

            string rawCurrency = _hud != null ? _hud.CurrencyDisplayText : "0 FICHAS";
            string formattedCurrency = FormatCurrencyHalo(rawCurrency);

            float ammoH = 28f;
            float tokenH = 20f;

            float ammoY = ry - ammoH;
            float lineY = ammoY - 6f;
            float tokenY = lineY - tokenH - 4f;

            // Fichas con sombreado nítido
            DrawTextWithShadow(new Rect(rx, tokenY, panelW, tokenH), $"FICHAS // {formattedCurrency}", _tokenStyle, ColorBoneText);

            // Línea horizontal divisoria de 2px de grosor
            DrawRect(new Rect(rx, lineY, panelW, 2f), ColorDividerLine);

            // Display de munición aislado con fuente grande y sombreado
            DrawTextWithShadow(new Rect(rx, ammoY, panelW, ammoH), $"AMMO // {formattedAmmo}", _ammoLargeStyle, ColorBoneText);

            // ----------------------------------------------------------------
            // ESQUINA INFERIOR IZQUIERDA: TELEMETRÍA SECUNDARIA
            // ----------------------------------------------------------------
            DrawTextWithShadow(new Rect(32f, Screen.height - 24f, 360f, 18f), "// SYS_LINK: [WASD] Mover | [LMB] Disparo | [TAB] Cursor", _terminalStyle, ColorTerminalDim);
        }

        // ====================================================================
        // DIBUJADO DE BARRA CON FONDO COMPLETO (TRACK AL 100%) & 14px ALTURA
        // ====================================================================
        private void DrawProgressBar(float x, float y, float width, float height, float fillPct, Color fillColor)
        {
            // 1. Fondo negro semitransparente (100% de la longitud de la barra)
            DrawRect(new Rect(x, y, width, height), ColorBarTrack);

            // 2. Relleno proporcional de la barra
            float clamped = Mathf.Clamp01(fillPct);
            if (clamped > 0f)
            {
                DrawRect(new Rect(x, y, width * clamped, height), fillColor);
            }

            // 3. Borde sutil del track para delimitar claramente el contenedor
            DrawHollowRect(new Rect(x, y, width, height), ColorTrackBorder, 1);
        }

        // ====================================================================
        // FORMATEO TÉCNICO DE CADENAS NUMÉRICAS
        // ====================================================================
        private string FormatAmmoHalo(string rawAmmo)
        {
            if (string.IsNullOrEmpty(rawAmmo)) return "[ -- / -- ]";

            if (rawAmmo.Contains("/"))
            {
                string[] parts = rawAmmo.Split('/');
                if (parts.Length == 2)
                {
                    string p0 = parts[0].Trim();
                    string p1 = parts[1].Trim();

                    bool ok0 = int.TryParse(p0, out int mag);
                    bool ok1 = int.TryParse(p1, out int res);

                    string s0 = ok0 ? mag.ToString("D2") : p0;
                    string s1 = ok1 ? res.ToString("D2") : p1;

                    return $"[ {s0} / {s1} ]";
                }
            }

            return $"[ {rawAmmo} ]";
        }

        private string FormatCurrencyHalo(string rawCurrency)
        {
            int amount = 0;
            if (GameManager.Instance != null)
            {
                amount = GameManager.Instance.Currency;
            }
            else if (!string.IsNullOrEmpty(rawCurrency))
            {
                string digits = System.Text.RegularExpressions.Regex.Match(rawCurrency, @"\d+").Value;
                int.TryParse(digits, out amount);
            }

            return $"[ {amount:D4} ]";
        }

        // ====================================================================
        // BARRA DE ENCUENTRO CON JEFE (ALTA VISIBILIDAD)
        // ====================================================================
        private void DrawBossBar()
        {
            float barW = 500f;
            float barH = 14f;
            float bx = (Screen.width - barW) * 0.5f;
            float by = 26f;

            string bossName = _bossUI.BossName;
            if (_bossUI.IsBossEnraged) bossName += " <color=#ff3333>[ENFURECIDO]</color>";

            DrawTextWithShadow(new Rect(bx, by, barW, 20f), $"» AMENAZA // {bossName.ToUpper()}", _bossHeaderStyle, ColorBoneText);
            by += 22f;

            DrawRect(new Rect(bx, by, barW, 2f), ColorDividerLine);
            by += 6f;

            DrawProgressBar(bx, by, barW, barH, _bossUI.HealthNormalized, ColorBloodRed);
        }

        // ====================================================================
        // DIÁLOGO DEL ELEVADOR FRONTERIZO
        // ====================================================================
        private void DrawElevatorDialog()
        {
            DrawRect(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.75f));

            float dialogW = 620f;
            float dialogH = 400f;
            float dx = (Screen.width - dialogW) * 0.5f;
            float dy = (Screen.height - dialogH) * 0.5f;

            Rect dialogRect = new Rect(dx, dy, dialogW, dialogH);
            DrawRect(dialogRect, new Color(0.06f, 0.06f, 0.07f, 0.96f));
            DrawHollowRect(dialogRect, ColorDividerLine, 1);

            float innerY = dy + 18f;
            DrawTextWithShadow(new Rect(dx + 20, innerY, dialogW - 40, 26), "🚪 CABINA DEL ELEVADOR FRONTERIZO", _bossHeaderStyle, ColorBoneText);
            innerY += 30f;
            DrawRect(new Rect(dx + 20, innerY, dialogW - 40, 2), ColorDividerLine);
            innerY += 14f;

            if (_elevatorUI.ActiveEventType == ElevatorResult.Merchant)
            {
                DrawTextWithShadow(new Rect(dx + 20, innerY, dialogW - 40, 20), "Un contrabandista te ofrece suministros a cambio de Fichas:", _labelStyle, ColorBoneText);
                innerY += 26f;

                var items = _elevatorUI.GetMerchantItems();
                if (items != null && items.Count > 0)
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        var it = items[i];
                        string desc = $"{it.DisplayName} — 💰 {it.Cost} Fichas\n<color=#aaaaaa>{it.Description}</color>";
                        if (GUI.Button(new Rect(dx + 30, innerY, dialogW - 60, 44), desc, _buttonStyle))
                        {
                            _elevatorUI.TryBuyItem(it);
                        }
                        innerY += 50f;
                    }
                }
                else
                {
                    DrawTextWithShadow(new Rect(dx + 30, innerY, dialogW - 60, 30), "El contrabandista se ha quedado sin mercancía.", _labelStyle, ColorBoneText);
                    innerY += 40f;
                }
            }
            else // Cursed Pact
            {
                DrawTextWithShadow(new Rect(dx + 20, innerY, dialogW - 40, 20), "Pacto Maldito: Obtén poder a cambio de mutar a los enemigos de la torre.", _labelStyle, new Color(0.95f, 0.4f, 0.4f));
                innerY += 26f;

                var pacts = _elevatorUI.GetOfferedPacts();
                if (pacts != null && pacts.Count > 0)
                {
                    float cardW = (dialogW - 60f) / Mathf.Max(1, pacts.Count);
                    for (int i = 0; i < pacts.Count; i++)
                    {
                        var p = pacts[i];
                        float cardX = dx + 30 + (i * (cardW + 10));
                        string pactCard = $"<b>{p.DisplayName}</b>\n\n" +
                                          $"<color=#66bb66>TU BUFF:</color> +{p.BonusDamage} Daño, +{p.BonusMoveSpeed} Vel\n\n" +
                                          $"<color=#bb5555>MUTADOR:</color> Daño x{p.EnemyDamageMultiplier}, {(p.GrantThrowDebris ? "Arrojan Escombros" : "Mayor HP")}";

                        GUI.Box(new Rect(cardX, innerY, cardW, 150), pactCard, _cardStyle);
                        if (GUI.Button(new Rect(cardX + 10, innerY + 108, cardW - 20, 32), "ACEPTAR PACTO", _buttonStyle))
                        {
                            _elevatorUI.AcceptPact(p);
                        }
                    }
                    innerY += 165f;
                }
            }

            // Botones inferiores
            if (GUI.Button(new Rect(dx + 30, dy + dialogH - 50, 240, 36), "SUBIR AL SIGUIENTE PISO", _buttonStyle))
            {
                _elevatorUI.AscendNextFloor();
            }

            if (_elevatorUI.ActiveEventType == ElevatorResult.CursedPact)
            {
                if (GUI.Button(new Rect(dx + 290, dy + dialogH - 50, 240, 36), "RECHAZAR PACTOS", _buttonStyle))
                {
                    _elevatorUI.DeclinePacts();
                }
            }
        }

        // ====================================================================
        // PRIMITIVAS DE RENDERIZADO VISUAL
        // ====================================================================
        private void DrawHollowRect(Rect rect, Color color, int thickness = 1)
        {
            if (thickness <= 0) return;

            // Borde Superior
            DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            // Borde Inferior
            DrawRect(new Rect(rect.x, rect.y + rect.height - thickness, rect.width, thickness), color);
            // Borde Izquierdo
            DrawRect(new Rect(rect.x, rect.y + thickness, thickness, rect.height - (thickness * 2)), color);
            // Borde Derecho
            DrawRect(new Rect(rect.x + rect.width - thickness, rect.y + thickness, thickness, rect.height - (thickness * 2)), color);
        }

        private void DrawRect(Rect rect, Color col)
        {
            Color old = GUI.color;
            GUI.color = col;
            GUI.DrawTexture(rect, _whiteTex);
            GUI.color = old;
        }

        private void OnDestroy()
        {
            if (_whiteTex != null) Destroy(_whiteTex);
        }
    }
}
