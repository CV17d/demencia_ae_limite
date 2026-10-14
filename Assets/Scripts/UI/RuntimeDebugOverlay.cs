// ============================================================================
// RuntimeDebugOverlay.cs — Immediate OnGUI HUD & Interactive Elevator Display
// ============================================================================
// Automatically renders in-game visual elements during Play Mode without needing
// Canvas/UI setup:
//   - Dynamic Health Bar (Green/Red)
//   - Mental Degradation Meter (Purple/Black) with Tier indicator
//   - Floor Progress, Currency & Weapon Ammunition
//   - Center Crosshair
//   - Interactive Elevator Dialog (Merchant purchases & Cursed Pact choices)
//   - Boss HP Bar on Boss Floors
// ============================================================================

using UnityEngine;
using DemenciaNoLimite.Core;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Core.Events;

namespace DemenciaNoLimite.UI
{
    public class RuntimeDebugOverlay : MonoBehaviour
    {
        private HUDController _hud;
        private ElevatorUIController _elevatorUI;
        private BossHealthBarUI _bossUI;

        private Texture2D _whiteTex;
        private GUIStyle _headerStyle;
        private GUIStyle _subStyle;
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
            if (_headerStyle != null) return;

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            _subStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = new Color(0.8f, 0.85f, 0.9f) }
            };

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold
            };

            _cardStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 13,
                wordWrap = true,
                padding = new RectOffset(12, 12, 12, 12)
            };
        }

        private void OnGUI()
        {
            InitStyles();

            // 1. Center Crosshair
            DrawCrosshair();

            // 2. Main In-game HUD
            DrawHUD();

            // 3. Boss Bar (if active)
            if (_bossUI != null && _bossUI.IsVisible)
            {
                DrawBossBar();
            }

            // 4. Elevator UI Dialog (if active)
            if (_elevatorUI != null && _elevatorUI.IsElevatorUIVisible)
            {
                DrawElevatorDialog();
            }
        }

        private void DrawCrosshair()
        {
            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            DrawRect(new Rect(cx - 1, cy - 8, 2, 16), new Color(1f, 1f, 1f, 0.7f));
            DrawRect(new Rect(cx - 8, cy - 1, 16, 2), new Color(1f, 1f, 1f, 0.7f));
        }

        private void DrawHUD()
        {
            float pad = 20f;
            float topY = pad;

            // Background panel for Top-Left Stats
            DrawRect(new Rect(pad - 8, topY - 8, 300, 160), new Color(0.08f, 0.08f, 0.12f, 0.85f));

            // Floor & Act
            string floorText = _hud != null ? _hud.FloorDisplayText : "TORRE DE LA FRONTERA";
            GUI.Label(new Rect(pad, topY, 280, 26), floorText, _headerStyle);
            topY += 28;

            // Health Bar
            var gm = GameManager.Instance;
            float hpPct = _hud != null ? _hud.HealthNormalized : 1f;
            string hpText = _hud != null ? _hud.HealthDisplayText : "100 / 100";

            GUI.Label(new Rect(pad, topY, 100, 20), $"SALUD:", _subStyle);
            DrawBar(pad + 65, topY + 2, 210, 16, hpPct, new Color(0.85f, 0.2f, 0.2f), new Color(0.2f, 0.05f, 0.05f));
            GUI.Label(new Rect(pad + 120, topY - 1, 120, 20), hpText, _subStyle);
            topY += 26;

            // Sanity Bar (Mental Degradation)
            float sanityPct = _hud != null ? _hud.SanityNormalized : 0f;
            string tierText = _hud != null ? _hud.SanityTierDisplayText : "STABLE";

            GUI.Label(new Rect(pad, topY, 100, 20), $"CORDURA:", _subStyle);
            DrawBar(pad + 65, topY + 2, 210, 16, 1f - sanityPct, new Color(0.6f, 0.2f, 0.9f), new Color(0.15f, 0.05f, 0.25f));
            GUI.Label(new Rect(pad + 115, topY - 1, 140, 20), $"{tierText} ({(sanityPct * 100f):F0}%)", _subStyle);
            topY += 26;

            // Currency & Ammo
            string currencyText = _hud != null ? _hud.CurrencyDisplayText : "0 FICHAS";
            string ammoText = _hud != null ? _hud.AmmoDisplayText : "-- / --";

            GUI.Label(new Rect(pad, topY, 140, 22), $"💰 {currencyText}", _subStyle);
            GUI.Label(new Rect(pad + 150, topY, 140, 22), $"🔫 BALAS: {ammoText}", _subStyle);
            topY += 24;

            // Controls Hint
            GUI.Label(new Rect(pad, topY, 280, 20), "<color=#888888>WASD: Mover | Shift: Correr | Click: Disparar | R: Recargar</color>", _subStyle);
        }

        private void DrawBossBar()
        {
            float barW = 550f;
            float barH = 26f;
            float bx = (Screen.width - barW) * 0.5f;
            float by = 35f;

            DrawRect(new Rect(bx - 10, by - 26, barW + 20, barH + 45), new Color(0.08f, 0.02f, 0.02f, 0.9f));

            string bossName = _bossUI.BossName;
            if (_bossUI.IsBossEnraged) bossName += " <color=red>[¡ENFURECIDO!]</color>";

            GUI.Label(new Rect(bx, by - 24, barW, 24), bossName, _headerStyle);
            DrawBar(bx, by, barW, barH, _bossUI.HealthNormalized, new Color(0.9f, 0.1f, 0.15f), new Color(0.2f, 0.05f, 0.05f));
        }

        private void DrawElevatorDialog()
        {
            // Dim background
            DrawRect(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.65f));

            float dialogW = 650f;
            float dialogH = 420f;
            float dx = (Screen.width - dialogW) * 0.5f;
            float dy = (Screen.height - dialogH) * 0.5f;

            DrawRect(new Rect(dx, dy, dialogW, dialogH), new Color(0.12f, 0.13f, 0.18f, 0.96f));

            float innerY = dy + 20f;
            GUI.Label(new Rect(dx + 20, innerY, dialogW - 40, 30), "🚪 CABINA DEL ELEVADOR FRONTERIZO", _headerStyle);
            innerY += 35f;

            if (_elevatorUI.ActiveEventType == ElevatorResult.Merchant)
            {
                GUI.Label(new Rect(dx + 20, innerY, dialogW - 40, 22), "Un contrabandista te ofrece suministros a cambio de Fichas:", _subStyle);
                innerY += 30f;

                var items = _elevatorUI.GetMerchantItems();
                if (items != null && items.Count > 0)
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        var it = items[i];
                        string desc = $"{it.DisplayName} — 💰 {it.Cost} Fichas\n<color=#aaaaaa>{it.Description}</color>";
                        if (GUI.Button(new Rect(dx + 30, innerY, dialogW - 60, 48), desc, _buttonStyle))
                        {
                            _elevatorUI.TryBuyItem(it);
                        }
                        innerY += 56f;
                    }
                }
                else
                {
                    GUI.Label(new Rect(dx + 30, innerY, dialogW - 60, 40), "El contrabandista se ha quedado sin mercancía.", _subStyle);
                    innerY += 50f;
                }
            }
            else // Cursed Pact
            {
                GUI.Label(new Rect(dx + 20, innerY, dialogW - 40, 22), "<color=#ff5555>Pacto Maldito: Obtén poder a cambio de mutar a los enemigos de la torre.</color>", _subStyle);
                innerY += 30f;

                var pacts = _elevatorUI.GetOfferedPacts();
                if (pacts != null && pacts.Count > 0)
                {
                    float cardW = (dialogW - 60f) / Mathf.Max(1, pacts.Count);
                    for (int i = 0; i < pacts.Count; i++)
                    {
                        var p = pacts[i];
                        float cardX = dx + 30 + (i * (cardW + 10));
                        string pactCard = $"<b>{p.DisplayName}</b>\n\n" +
                                          $"<color=#55ff55>TU BUFF:</color> +{p.BonusDamage} Daño, +{p.BonusMoveSpeed} Vel\n\n" +
                                          $"<color=#ff5555>MUTADOR ENEMIGO:</color> Daño x{p.EnemyDamageMultiplier}, {(p.GrantThrowDebris ? "Arrojan Escombros" : "Mayor HP")}";

                        GUI.Box(new Rect(cardX, innerY, cardW, 160), pactCard, _cardStyle);
                        if (GUI.Button(new Rect(cardX + 10, innerY + 115, cardW - 20, 36), "ACEPTAR PACTO", _buttonStyle))
                        {
                            _elevatorUI.AcceptPact(p);
                        }
                    }
                    innerY += 175f;
                }
            }

            // Bottom Buttons
            if (GUI.Button(new Rect(dx + 40, dy + dialogH - 55, 240, 40), "SUBIR AL SIGUIENTE PISO", _buttonStyle))
            {
                _elevatorUI.AscendNextFloor();
            }

            if (_elevatorUI.ActiveEventType == ElevatorResult.CursedPact)
            {
                if (GUI.Button(new Rect(dx + 300, dy + dialogH - 55, 240, 40), "RECHAZAR PACTOS", _buttonStyle))
                {
                    _elevatorUI.DeclinePacts();
                }
            }
        }

        private void DrawBar(float x, float y, float w, float h, float fillPct, Color fillCol, Color bgCol)
        {
            DrawRect(new Rect(x, y, w, h), bgCol);
            DrawRect(new Rect(x, y, w * Mathf.Clamp01(fillPct), h), fillCol);
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
