/*
 * LifeAlertPlus — ESP32-C3 Mini Firmware
 *
 * Sensors  : MPU6050 (accel/gyro), MAX30100 (SpO2/HR), DS18B20 (temp), NEO-6M GPS
 * Display  : OLED SSD1306 via PCA9548 multiplexer (ch 1)
 * Network  : WiFi → HTTPS POST to LifeAlertPlus API every API_POST_MS ms
 *
 * GPIO map (ESP32-C3 Mini):
 *   GPIO 0  — DS18B20 (1-Wire)
 *   GPIO 1  — GPS TX (NEO-6M)    → ESP RX  (UART1 RX)
 *   GPIO 2  — GPS RX (NEO-6M)    → ESP TX  (UART1 TX)
 *   GPIO 3  — Button 1 (panic alert)
 *   GPIO 4  — Button 2 (sleep toggle)
 *   GPIO 5  — LED yellow (problem / offline / sleep)
 *   GPIO 6  — LED green  (system on)
 *   GPIO 8  — I2C SDA (TCA9548)
 *   GPIO 9  — I2C SCL (TCA9548)
 */

#include <stdio.h>
#include <math.h>
#include <string.h>
#include <stdlib.h>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "freertos/event_groups.h"
#include "freertos/semphr.h"
#include "driver/i2c.h"
#include "driver/uart.h"
#include "driver/gpio.h"
#include "esp_rom_sys.h"
#include "esp_system.h"
#include "esp_efuse.h"
#include "esp_mac.h"
#include "esp_wifi.h"
#include "esp_event.h"
#include "esp_log.h"
#include "esp_crt_bundle.h"
#include "esp_http_client.h"
#include "nvs_flash.h"
#include "nvs.h"
#include "esp_timer.h"
#include "lwip/inet.h"
#include "lwip/dns.h"
#include "cJSON.h"
#include "esp_http_server.h"

// ─── WiFi / API ───────────────────────────────────────────────────────────────
// All secrets / deployment-specific values come from sdkconfig (gitignored),
// generated from menuconfig options defined in main/Kconfig.projbuild.
// Real values live in the developer's local sdkconfig; sdkconfig.defaults
// committed to git has only empty placeholders.
#define WIFI_SSID_DEFAULT  CONFIG_LIFEALERT_DEFAULT_WIFI_SSID
#define WIFI_PASS_DEFAULT  CONFIG_LIFEALERT_DEFAULT_WIFI_PASS
#define WIFI_MAX_RETRY     5

#define API_BASE_URL         CONFIG_LIFEALERT_API_BASE_URL
#define API_INGEST_PATH      "/api/ESP/ingest"
#define API_PANIC_PATH       "/api/ESP/panic"
#define API_HEARTBEAT_PATH   "/api/ESP/heartbeat"
#define API_WIFI_CONFIG_PATH "/api/ESP/wifi-config/"  // serial appended at call site
#define API_DEVICE_KEY       CONFIG_LIFEALERT_API_DEVICE_KEY
#define FIRMWARE_VERSION     "1.1.0"

// ─── Timing ───────────────────────────────────────────────────────────────────
#define SENSOR_READ_MS     500      // inner loop period
#define API_POST_MS        5000     // how often to POST measurements
#define HEARTBEAT_MS       300000   // heartbeat every 5 minutes
// În sleep mode nu se trimit date — SLEEP_POST_MS eliminat

// ─── Fall detection (MPU6050) ─────────────────────────────────────────────────
// Threshold-based detector: free-fall (low |a|) → impact spike → post-impact stillness.
#define FALL_SAMPLE_MS              20      // ~50 Hz polling
#define FALL_FREEFALL_THRESHOLD_G   0.5f    // |a| below this = free-fall in progress
#define FALL_FREEFALL_MIN_MS        80      // sustained free-fall before impact accepted
#define FALL_FREEFALL_MAX_MS        800     // max free-fall window (anything longer = abort)
#define FALL_IMPACT_THRESHOLD_G     2.5f    // impact spike on landing
#define FALL_STILLNESS_THRESHOLD_G  0.25f   // |a-1g| deviation tolerated while "lying still"
#define FALL_STILLNESS_DURATION_MS  1500    // sustained stillness needed to confirm fall

// ─── Offline queue (NVS) ──────────────────────────────────────────────────────
#define QUEUE_NS              "offl_q"
#define QUEUE_MAX             50
#define QUEUE_FLUSH_PER_CYCLE  5    // max cereri per ciclu de POST

// Coadă separată pentru panic (alertele sunt rare dar critice — au prioritate)
#define PANIC_QUEUE_NS        "panic_q"
#define PANIC_QUEUE_MAX       10

// ─── I2C ──────────────────────────────────────────────────────────────────────
#define I2C_SDA_PIN      8
#define I2C_SCL_PIN      9
#define I2C_PORT         I2C_NUM_0
#define I2C_FREQ_HZ      100000
#define I2C_TIMEOUT_MS   50

// ─── TCA9548 multiplexer ──────────────────────────────────────────────────────
#define MUX_ADDR_MIN     0x70
#define MUX_ADDR_MAX     0x77
#define MUX_CH_MAX30100  0    // SD0 / SC0
#define MUX_CH_OLED      1    // SD1 / SC1
#define MUX_CH_MPU6050   2    // SD2 / SC2

// ─── MPU6050 ──────────────────────────────────────────────────────────────────
#define MPU_ADDR         0x68
#define MPU_ADDR_ALT     0x69
#define MPU_PWR_MGMT_1   0x6B
#define MPU_ACCEL_XOUT_H 0x3B

// ─── MAX30100 ─────────────────────────────────────────────────────────────────
#define M30_ADDR         0x57
#define M30_FIFO_DATA    0x05
#define M30_MODE_CFG     0x06
#define M30_SPO2_CFG     0x07
#define M30_LED_CFG      0x09
#define M30_FIFO_WR      0x02
#define MAX30100_FINGER_THRESH  1000   // IR sub acest prag = fără deget pe senzor

// ─── OLED SSD1306 ─────────────────────────────────────────────────────────────
#define OLED_ADDR        0x3C
#define OLED_CMD_BYTE    0x00
#define OLED_DATA_BYTE   0x40

// ─── DS18B20 (1-Wire) ─────────────────────────────────────────────────────────
#define DS18B20_PIN      0
#define DS18B20_CONV_MS  750

// ─── GPS NEO-6M ───────────────────────────────────────────────────────────────
#define GPS_UART_NUM     UART_NUM_1
#define GPS_TX_PIN       2    // ESP TX → NEO-6M RX
#define GPS_RX_PIN       1    // ESP RX ← NEO-6M TX
#define GPS_RX_BUF       1024

// ─── GPIO ─────────────────────────────────────────────────────────────────────
#define BUTTON1_PIN      3    // panic alert
#define BUTTON2_PIN      4    // sleep toggle
#define LED_YELLOW_PIN   5    // problem / offline / sleep indicator
#define LED_GREEN_PIN    6    // system on indicator

#define HW_SELFTEST      0    // diagnostic LED verde + butoane la boot (dezactivat)

// =============================================================================
// Global state
// =============================================================================
static uint8_t g_mux_addr = MUX_ADDR_MIN;
static uint8_t g_mpu_addr = MPU_ADDR;
static char    g_serial[32];

// WiFi
static EventGroupHandle_t s_wifi_eg;
#define WIFI_OK_BIT    BIT0
#define WIFI_FAIL_BIT  BIT1
static volatile bool s_wifi_ok    = false;
static int           s_wifi_retry = 0;

// GPS
typedef struct {
    bool  valid, has_nmea;
    float lat, lon, speed_kmh;
    int   fix_quality, satellites;
    char  time_str[12], date_str[10];
} gps_state_t;

static gps_state_t g_gps        = {};
static char        g_gps_line[256];
static int         g_gps_line_n = 0;
static uint8_t     g_gps_rx[GPS_RX_BUF];

// Buttons
static QueueHandle_t s_btn_q     = NULL;
static int64_t       s_btn_last_us[2] = {}; // debounce timestamps per button
static bool          s_btn_disabled[2] = { false, false }; // [0]=panic, [1]=sleep

// Power-save mode (Button 2 toggle)
static volatile bool g_sleep_mode = false;

// Heartbeat
static esp_timer_handle_t s_hb_timer = NULL;

// Sleep auto-off: după 5s în sleep mode, stinge LED galben + OLED display
static esp_timer_handle_t s_sleep_off_timer = NULL;
#define SLEEP_OFF_DELAY_MS  5000

// Senzori — status init la boot (folosit pentru afişarea erorilor pe OLED)
static bool g_oled_ok = false;
static bool g_mpu_ok  = false;
static bool g_max_ok  = false;
static bool g_ds_ok   = false;

// Mutex I2C recursiv — serializează accesul la magistrală + mux între task-uri
// concurente (main loop, pulse_task, btn_task). Recursiv pentru ca o funcţie de nivel
// înalt (oled_show_data) să poată ţine lock-ul peste mai multe apeluri imbricate
// (oled_write_str), garantând că un draw OLED complet nu e întrerupt de pulse_task.
static SemaphoreHandle_t s_i2c_lock = NULL;
static inline void i2c_lock(void)   { if (s_i2c_lock) xSemaphoreTakeRecursive(s_i2c_lock, portMAX_DELAY); }
static inline void i2c_unlock(void) { if (s_i2c_lock) xSemaphoreGiveRecursive(s_i2c_lock); }

// Date publicate de pulse_task — citite de main loop / OLED
static volatile uint16_t g_last_ir       = 0;
static volatile uint16_t g_last_red      = 0;
static volatile int      g_bpm           = 0;     // 0 = necalculat / fără deget
static volatile int      g_spo2          = 0;     // 0 = necalculat / fără deget
static volatile bool     g_finger_on     = false; // IR peste prag

// Fall detector — setat de fall_task, citit (şi resetat) de build_json
static volatile bool g_fall_detected = false;

// ─── Movement summary (umple fall_task la 50 Hz, citeşte build_json la fiecare POST) ──
// Folosim acelaşi flux de samples al fall_task ca să clasificăm la fiecare ciclu de POST
// dacă persoana se mişcă sau e nemişcată — fără să facem un al doilea task / al doilea
// read pe MPU. La fiecare POST: media abaterii |a-1g| peste fereastra de samples + maximul;
// build_json combină valorile cu un prag şi pune "activity":"moving" sau "stationary".
typedef struct {
    uint32_t sample_count;
    float    dev_sum;   // suma cumulată |mag - 1g|
    float    dev_max;   // max |mag - 1g| din fereastră
} movement_accumulator_t;

static movement_accumulator_t g_movement       = {0, 0.0f, 0.0f};
static SemaphoreHandle_t      s_movement_lock  = NULL;

// Praguri pentru clasificarea în firmware. Zgomotul senzorului la repaus rămâne ~0.02-0.05g,
// mersul produce 0.1-0.3g abatere medie, alergarea 0.4g+; vârfuri scurte de 0.2g indică
// mişcări izolate (ridicat în picioare, întins de braţ) chiar dacă media e mică.
#define MOVEMENT_MEAN_THRESHOLD_G  0.05f
#define MOVEMENT_PEAK_THRESHOLD_G  0.20f

// Netif handle — necesar pentru a seta DNS backup după connect
static esp_netif_t *s_wifi_netif = NULL;

// Offline queue counters (used by oled_show_data and queue functions)
static uint32_t s_q_head = 0, s_q_tail = 0, s_q_count = 0;

// Panic queue counters (NVS handle declarat lângă funcţiile sale)
static uint32_t s_pq_head = 0, s_pq_tail = 0, s_pq_count = 0;

// =============================================================================
// I2C
// =============================================================================
static void i2c_setup(void)
{
    i2c_config_t c = {};
    c.mode             = I2C_MODE_MASTER;
    c.sda_io_num       = I2C_SDA_PIN;
    c.scl_io_num       = I2C_SCL_PIN;
    c.sda_pullup_en    = GPIO_PULLUP_ENABLE;
    c.scl_pullup_en    = GPIO_PULLUP_ENABLE;
    c.master.clk_speed = I2C_FREQ_HZ;
    i2c_param_config(I2C_PORT, &c);
    i2c_driver_install(I2C_PORT, c.mode, 0, 0, 0);
}

static bool i2c_probe(uint8_t addr)
{
    i2c_cmd_handle_t h = i2c_cmd_link_create();
    i2c_master_start(h);
    i2c_master_write_byte(h, (addr << 1) | I2C_MASTER_WRITE, true);
    i2c_master_stop(h);
    bool ok = (i2c_master_cmd_begin(I2C_PORT, h, pdMS_TO_TICKS(I2C_TIMEOUT_MS)) == ESP_OK);
    i2c_cmd_link_delete(h);
    return ok;
}

// =============================================================================
// PCA9548 — close channel after every read to prevent I2C bus conflicts
// =============================================================================
static bool mux_setup(void)
{
    for (uint8_t a = MUX_ADDR_MIN; a <= MUX_ADDR_MAX; a++) {
        if (i2c_probe(a)) { g_mux_addr = a; return true; }
    }
    return false;
}

static void mux_open(uint8_t ch)
{
    uint8_t d = (ch < 8) ? (uint8_t)(1u << ch) : 0u;
    i2c_master_write_to_device(I2C_PORT, g_mux_addr, &d, 1, pdMS_TO_TICKS(I2C_TIMEOUT_MS));
    vTaskDelay(pdMS_TO_TICKS(5));
}

static void mux_close(void)
{
    uint8_t d = 0x00;
    i2c_master_write_to_device(I2C_PORT, g_mux_addr, &d, 1, pdMS_TO_TICKS(I2C_TIMEOUT_MS));
}

// =============================================================================
// MPU6050
// =============================================================================
static bool mpu_setup(void)
{
    mux_open(MUX_CH_MPU6050);
    vTaskDelay(pdMS_TO_TICKS(10));
    const uint8_t candidates[] = { MPU_ADDR, MPU_ADDR_ALT };
    for (int i = 0; i < 2; i++) {
        if (!i2c_probe(candidates[i])) continue;
        g_mpu_addr = candidates[i];
        uint8_t wake[2] = { MPU_PWR_MGMT_1, 0x00 };
        esp_err_t e = i2c_master_write_to_device(I2C_PORT, g_mpu_addr, wake, 2, pdMS_TO_TICKS(I2C_TIMEOUT_MS));
        vTaskDelay(pdMS_TO_TICKS(20));
        mux_close();
        return e == ESP_OK;
    }
    mux_close();
    return false;
}

static bool mpu_read(int16_t *accel, int16_t *gyro)
{
    i2c_lock();
    mux_open(MUX_CH_MPU6050);
    uint8_t reg = MPU_ACCEL_XOUT_H;
    uint8_t d[14];
    esp_err_t e = i2c_master_write_read_device(
        I2C_PORT, g_mpu_addr, &reg, 1, d, 14, pdMS_TO_TICKS(I2C_TIMEOUT_MS));
    mux_close();
    i2c_unlock();
    if (e != ESP_OK) return false;
    accel[0] = (int16_t)((d[0]  << 8) | d[1]);
    accel[1] = (int16_t)((d[2]  << 8) | d[3]);
    accel[2] = (int16_t)((d[4]  << 8) | d[5]);
    gyro[0]  = (int16_t)((d[8]  << 8) | d[9]);
    gyro[1]  = (int16_t)((d[10] << 8) | d[11]);
    gyro[2]  = (int16_t)((d[12] << 8) | d[13]);
    return true;
}

// =============================================================================
// MAX30100 — kept in continuous SpO2 mode; resetting between reads degrades accuracy
// =============================================================================
static bool max_setup(void)
{
    mux_open(MUX_CH_MAX30100);
    vTaskDelay(pdMS_TO_TICKS(10));
    if (!i2c_probe(M30_ADDR)) { mux_close(); return false; }

    uint8_t d[2];
    d[0] = M30_MODE_CFG; d[1] = 0x40;   // reset
    i2c_master_write_to_device(I2C_PORT, M30_ADDR, d, 2, pdMS_TO_TICKS(I2C_TIMEOUT_MS));
    vTaskDelay(pdMS_TO_TICKS(50));
    d[0] = M30_MODE_CFG; d[1] = 0x03;   // SpO2 mode (continuous)
    i2c_master_write_to_device(I2C_PORT, M30_ADDR, d, 2, pdMS_TO_TICKS(I2C_TIMEOUT_MS));
    d[0] = M30_SPO2_CFG; d[1] = 0x27;   // 1600 Hz, 16-bit ADC
    i2c_master_write_to_device(I2C_PORT, M30_ADDR, d, 2, pdMS_TO_TICKS(I2C_TIMEOUT_MS));
    d[0] = M30_LED_CFG;  d[1] = 0x47;   // RED=4.4mA, IR=27.1mA — semnal PPG bun, consum redus pe baterie
    i2c_master_write_to_device(I2C_PORT, M30_ADDR, d, 2, pdMS_TO_TICKS(I2C_TIMEOUT_MS));
    d[0] = M30_FIFO_WR;  d[1] = 0x00;   // clear FIFO
    i2c_master_write_to_device(I2C_PORT, M30_ADDR, d, 2, pdMS_TO_TICKS(I2C_TIMEOUT_MS));
    mux_close();
    return true;
}

static bool max_read(uint16_t *ir, uint16_t *red)
{
    i2c_lock();
    mux_open(MUX_CH_MAX30100);
    uint8_t reg = M30_FIFO_DATA;
    uint8_t d[4];
    esp_err_t e = i2c_master_write_read_device(
        I2C_PORT, M30_ADDR, &reg, 1, d, 4, pdMS_TO_TICKS(I2C_TIMEOUT_MS));
    mux_close();
    i2c_unlock();
    if (e != ESP_OK) return false;
    *ir  = (uint16_t)((d[0] << 8) | d[1]);
    *red = (uint16_t)((d[2] << 8) | d[3]);
    return true;
}

static void max_sleep(void)
{
    i2c_lock();
    mux_open(MUX_CH_MAX30100);
    uint8_t d[2] = { M30_MODE_CFG, 0x80 };   // SHDN bit — shutdown corect
    i2c_master_write_to_device(I2C_PORT, M30_ADDR, d, 2, pdMS_TO_TICKS(I2C_TIMEOUT_MS));
    mux_close();
    i2c_unlock();
}

static void max_wake(void)
{
    i2c_lock();
    mux_open(MUX_CH_MAX30100);
    uint8_t d[2];
    d[0] = M30_MODE_CFG; d[1] = 0x03;   // SpO2 mode, clear SHDN
    i2c_master_write_to_device(I2C_PORT, M30_ADDR, d, 2, pdMS_TO_TICKS(I2C_TIMEOUT_MS));
    d[0] = M30_LED_CFG;  d[1] = 0x24;   // rescrie curentul LED (se poate reseta la SHDN)
    i2c_master_write_to_device(I2C_PORT, M30_ADDR, d, 2, pdMS_TO_TICKS(I2C_TIMEOUT_MS));
    mux_close();
    i2c_unlock();
    vTaskDelay(pdMS_TO_TICKS(50));       // timp pentru ADC + umplere FIFO
}

// =============================================================================
// Pulse task — eşantionează MAX30100 la ~100Hz şi calculează BPM
// =============================================================================
//   Algoritm:
//     1) eşantionează IR la fiecare 10ms (rata MAX30100 = 100sps în SpO2 mode);
//     2) urmăreşte componenta DC printr-o medie mobilă exponenţială (EMA);
//     3) componenta AC = sample - DC;
//     4) detecţie de vârfuri: tranziţie din "sub prag" în "peste prag" pe AC;
//     5) BPM = 60000 / medie(intervale_intre_varfuri) [ms].
//
//   Rezultatul (g_bpm) este afişat pe OLED şi trimis în JSON la fiecare POST.
// =============================================================================
static void pulse_task(void *)
{
    float dc_ir          = 0.0f;
    float dc_red         = 0.0f;
    const float DC_ALPHA = 0.01f;   // ~100 samples (~1s) time constant
    bool  above          = false;
    int   last_peak_ms   = 0;
    int   intervals[6]   = {0};
    int   int_idx        = 0;
    int   int_filled     = 0;

    // Peak/valley trackers per bătaie — pentru calculul amplitudinii AC
    float ir_min  = 0.0f, ir_max  = 0.0f;
    float red_min = 0.0f, red_max = 0.0f;
    bool  beat_started = false;

    while (true) {
        if (g_sleep_mode || !g_max_ok) {
            g_bpm = 0; g_spo2 = 0;
            g_finger_on = false;
            dc_ir = 0.0f; dc_red = 0.0f;
            above = false; int_filled = 0; int_idx = 0;
            last_peak_ms = 0; beat_started = false;
            vTaskDelay(pdMS_TO_TICKS(300));
            continue;
        }

        uint16_t ir = 0, red = 0;
        if (!max_read(&ir, &red)) {
            vTaskDelay(pdMS_TO_TICKS(20));
            continue;
        }
        g_last_ir  = ir;
        g_last_red = red;

        bool finger = (ir > MAX30100_FINGER_THRESH);
        g_finger_on = finger;

        if (!finger) {
            g_bpm = 0; g_spo2 = 0;
            dc_ir = 0.0f; dc_red = 0.0f;
            above = false; int_filled = 0; int_idx = 0;
            last_peak_ms = 0; beat_started = false;
            vTaskDelay(pdMS_TO_TICKS(50));
            continue;
        }

        // DC tracking (EMA) pentru IR și RED
        if (dc_ir  == 0.0f) { dc_ir  = (float)ir;  }
        if (dc_red == 0.0f) { dc_red = (float)red; }
        dc_ir  = dc_ir  * (1.0f - DC_ALPHA) + (float)ir  * DC_ALPHA;
        dc_red = dc_red * (1.0f - DC_ALPHA) + (float)red * DC_ALPHA;

        float ac = (float)ir - dc_ir;

        // Tracking peak/valley per bătaie pentru SpO2
        if (!beat_started) {
            ir_min = ir_max = (float)ir;
            red_min = red_max = (float)red;
            beat_started = true;
        } else {
            if ((float)ir  > ir_max)  ir_max  = (float)ir;
            if ((float)ir  < ir_min)  ir_min  = (float)ir;
            if ((float)red > red_max) red_max = (float)red;
            if ((float)red < red_min) red_min = (float)red;
        }

        float peak_th = dc_ir * 0.005f;
        if (peak_th < 50.0f) peak_th = 50.0f;

        int now_ms = xTaskGetTickCount() * portTICK_PERIOD_MS;

        if (!above && ac > peak_th) {
            above = true;
            if (last_peak_ms > 0) {
                int dt = now_ms - last_peak_ms;
                if (dt > 300 && dt < 1500) {
                    intervals[int_idx] = dt;
                    int_idx = (int_idx + 1) % 6;
                    if (int_filled < 6) int_filled++;
                    int sum = 0;
                    for (int i = 0; i < int_filled; i++) sum += intervals[i];
                    int avg_dt = sum / int_filled;
                    int bpm = 60000 / avg_dt;
                    if (bpm >= 40 && bpm <= 200) g_bpm = bpm;

                    // SpO2 = R = (RED_AC/RED_DC) / (IR_AC/IR_DC)
                    // SpO2% ≈ 110 - 25*R  (model liniar empiric)
                    float ir_ac  = ir_max  - ir_min;
                    float red_ac = red_max - red_min;
                    if (dc_ir > 500.0f && dc_red > 100.0f && ir_ac > 30.0f && red_ac > 10.0f) {
                        float R = (red_ac / dc_red) / (ir_ac / dc_ir);
                        int spo2 = (int)(110.0f - 25.0f * R + 0.5f);
                        if (spo2 > 100) spo2 = 100;
                        if (spo2 <  70) spo2 =  70;
                        g_spo2 = spo2;
                    }

                    // Reset trackerele pentru următoarea bătaie
                    ir_min = ir_max = (float)ir;
                    red_min = red_max = (float)red;
                }
            }
            last_peak_ms = now_ms;
        } else if (above && ac < -peak_th) {
            above = false;
        }

        vTaskDelay(pdMS_TO_TICKS(10));  // ~100 Hz polling
    }
}

// =============================================================================
// Fall detection task — eşantionează MPU6050 la ~50 Hz şi rulează state machine:
//   IDLE → FREE_FALL → STILLNESS_MONITOR → confirm (sau abort).
// Magnitudinea acceleraţiei (în g) sub un prag = free-fall; un spike după = impact;
// dacă persoana rămâne imobilă (|a| ~ 1g, fără variaţii) câteva secunde după impact,
// considerăm căderea confirmată. Setează flag-ul g_fall_detected — main loop îl citeşte
// la următorul POST şi îl resetează după ce l-a inclus în payload.
// =============================================================================
typedef enum {
    FALL_IDLE,
    FALL_IN_FREEFALL,
    FALL_MONITORING_STILLNESS
} fall_state_t;

static void fall_task(void *)
{
    fall_state_t state         = FALL_IDLE;
    int64_t      state_start_ms = 0;
    int64_t      freefall_start_ms = 0;

    while (true) {
        // În sleep mode sau dacă MPU nu a iniţializat: stăm pe loc şi resetăm starea
        if (g_sleep_mode || !g_mpu_ok) {
            state = FALL_IDLE;
            vTaskDelay(pdMS_TO_TICKS(300));
            continue;
        }

        int16_t accel[3], gyro[3];
        if (!mpu_read(accel, gyro)) {
            vTaskDelay(pdMS_TO_TICKS(FALL_SAMPLE_MS));
            continue;
        }

        // MPU6050 implicit pe ±2g, 16384 LSB/g
        float ax  = accel[0] / 16384.0f;
        float ay  = accel[1] / 16384.0f;
        float az  = accel[2] / 16384.0f;
        float mag = sqrtf(ax * ax + ay * ay + az * az);

        // Acumulăm abaterea de la 1g (gravitaţie) pentru clasificarea activităţii.
        // build_json consumă acumulatorul atomic la fiecare POST şi îl resetează.
        // Eşec la take (timeout 0) = sărim sample-ul, nu blocăm fall detector-ul.
        float dev = fabsf(mag - 1.0f);
        if (s_movement_lock && xSemaphoreTake(s_movement_lock, 0) == pdTRUE) {
            g_movement.sample_count++;
            g_movement.dev_sum += dev;
            if (dev > g_movement.dev_max) g_movement.dev_max = dev;
            xSemaphoreGive(s_movement_lock);
        }

        int64_t now_ms = esp_timer_get_time() / 1000;

        switch (state) {
            case FALL_IDLE:
                if (mag < FALL_FREEFALL_THRESHOLD_G) {
                    state = FALL_IN_FREEFALL;
                    state_start_ms    = now_ms;
                    freefall_start_ms = now_ms;
                }
                break;

            case FALL_IN_FREEFALL:
                if (mag >= FALL_IMPACT_THRESHOLD_G) {
                    // Impact: acceptat doar dacă free-fall-ul a durat suficient
                    if ((now_ms - freefall_start_ms) >= FALL_FREEFALL_MIN_MS) {
                        state          = FALL_MONITORING_STILLNESS;
                        state_start_ms = now_ms;
                        printf("[FALL] Impact dupa %lldms free-fall (|a|=%.2fg)\n",
                               now_ms - freefall_start_ms, mag);
                    } else {
                        state = FALL_IDLE;  // free-fall prea scurt = fals trigger
                    }
                } else if (mag > FALL_FREEFALL_THRESHOLD_G) {
                    // Ieşit din free-fall fără impact = mişcare normală
                    state = FALL_IDLE;
                } else if ((now_ms - state_start_ms) > FALL_FREEFALL_MAX_MS) {
                    // Free-fall prelungit = senzor blocat / scenariu atipic
                    state = FALL_IDLE;
                }
                break;

            case FALL_MONITORING_STILLNESS: {
                // |a| ar trebui ~1g (gravitaţia) cu variaţii minime dacă persoana e jos
                float dev = fabsf(mag - 1.0f);
                if (dev > FALL_STILLNESS_THRESHOLD_G) {
                    // Încă se mişcă — resetăm contorul de stillness
                    state_start_ms = now_ms;
                }
                if ((now_ms - state_start_ms) >= FALL_STILLNESS_DURATION_MS) {
                    g_fall_detected = true;
                    printf("[FALL] CONFIRMAT — raportez la urmatorul POST\n");
                    state = FALL_IDLE;
                }
                break;
            }
        }

        vTaskDelay(pdMS_TO_TICKS(FALL_SAMPLE_MS));
    }
}

// =============================================================================
// OLED SSD1306
// =============================================================================
static void oled_cmd(uint8_t cmd)
{
    uint8_t b[2] = { OLED_CMD_BYTE, cmd };
    i2c_master_write_to_device(I2C_PORT, OLED_ADDR, b, 2, pdMS_TO_TICKS(I2C_TIMEOUT_MS));
}

static bool oled_setup(void)
{
    for (int i = 0; i < 3; i++) {
        mux_open(MUX_CH_OLED);
        vTaskDelay(pdMS_TO_TICKS(10));
        if (i2c_probe(OLED_ADDR)) break;
        mux_close();
        if (i == 2) return false;
        vTaskDelay(pdMS_TO_TICKS(20));
    }
    vTaskDelay(pdMS_TO_TICKS(100));
    static const uint8_t seq[] = {
        0xAE, 0x20, 0x10, 0xB0, 0xC8, 0x00, 0x10, 0x40,
        0x81, 0xFF, 0xA1, 0xA6, 0xA8, 0x3F, 0xA4, 0xD3,
        0x00, 0xD5, 0xF0, 0xD9, 0x22, 0xDA, 0x12, 0xDB,
        0x20, 0x8D, 0x14, 0xAF
    };
    for (size_t i = 0; i < sizeof(seq); i++) oled_cmd(seq[i]);
    mux_close();
    return true;
}

static void oled_clear(void)
{
    i2c_lock();
    mux_open(MUX_CH_OLED);
    for (int p = 0; p < 8; p++) {
        oled_cmd((uint8_t)(0xB0 + p));
        oled_cmd(0x00); oled_cmd(0x10);
        for (int c = 0; c < 128; c++) {
            uint8_t b[2] = { OLED_DATA_BYTE, 0x00 };
            i2c_master_write_to_device(I2C_PORT, OLED_ADDR, b, 2, pdMS_TO_TICKS(I2C_TIMEOUT_MS));
        }
    }
    mux_close();
    i2c_unlock();
}

// 8×8 bitmap for "LifeAlertPlus" (13 chars)
static const uint8_t oled_font[][8] = {
    {0x7F, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x00}, // L
    {0x00, 0x00, 0x44, 0x7D, 0x40, 0x00, 0x00, 0x00}, // i
    {0x7C, 0x04, 0x02, 0x02, 0x04, 0x00, 0x00, 0x00}, // f
    {0x38, 0x54, 0x54, 0x54, 0x18, 0x00, 0x00, 0x00}, // e
    {0x7E, 0x09, 0x09, 0x09, 0x7E, 0x00, 0x00, 0x00}, // A
    {0x00, 0x41, 0x7F, 0x40, 0x00, 0x00, 0x00, 0x00}, // l
    {0x38, 0x54, 0x54, 0x54, 0x18, 0x00, 0x00, 0x00}, // e
    {0x7C, 0x08, 0x04, 0x04, 0x08, 0x00, 0x00, 0x00}, // r
    {0x04, 0x02, 0x7F, 0x02, 0x04, 0x00, 0x00, 0x00}, // t
    {0x7F, 0x09, 0x09, 0x09, 0x06, 0x00, 0x00, 0x00}, // P
    {0x00, 0x41, 0x7F, 0x40, 0x00, 0x00, 0x00, 0x00}, // l
    {0x3C, 0x40, 0x40, 0x20, 0x7C, 0x00, 0x00, 0x00}, // u
    {0x48, 0x54, 0x54, 0x54, 0x24, 0x00, 0x00, 0x00}, // s
};

static void oled_show_title(void)
{
    i2c_lock();
    mux_open(MUX_CH_OLED);
    oled_cmd(0xB0 + 3);         // middle row
    oled_cmd(0x10); oled_cmd(0x02);
    for (int i = 0; i < 13; i++) {
        for (int j = 0; j < 8; j++) {
            uint8_t b[2] = { OLED_DATA_BYTE, oled_font[i][j] };
            i2c_master_write_to_device(I2C_PORT, OLED_ADDR, b, 2, pdMS_TO_TICKS(I2C_TIMEOUT_MS));
        }
    }
    mux_close();
    i2c_unlock();
}

// 5×7 font — ASCII 32-127, 5 bytes per glyph (column-major, LSB=top)
static const uint8_t g_font5x7[][5] = {
    {0x00,0x00,0x00,0x00,0x00},{0x00,0x00,0x5F,0x00,0x00},{0x00,0x07,0x00,0x07,0x00},
    {0x14,0x7F,0x14,0x7F,0x14},{0x24,0x2A,0x7F,0x2A,0x12},{0x23,0x13,0x08,0x64,0x62},
    {0x36,0x49,0x55,0x22,0x50},{0x00,0x05,0x03,0x00,0x00},{0x00,0x1C,0x22,0x41,0x00},
    {0x00,0x41,0x22,0x1C,0x00},{0x14,0x08,0x3E,0x08,0x14},{0x08,0x08,0x3E,0x08,0x08},
    {0x00,0x50,0x30,0x00,0x00},{0x08,0x08,0x08,0x08,0x08},{0x00,0x60,0x60,0x00,0x00},
    {0x20,0x10,0x08,0x04,0x02},{0x3E,0x51,0x49,0x45,0x3E},{0x00,0x42,0x7F,0x40,0x00},
    {0x42,0x61,0x51,0x49,0x46},{0x21,0x41,0x45,0x4B,0x31},{0x18,0x14,0x12,0x7F,0x10},
    {0x27,0x45,0x45,0x45,0x39},{0x3C,0x4A,0x49,0x49,0x30},{0x01,0x71,0x09,0x05,0x03},
    {0x36,0x49,0x49,0x49,0x36},{0x06,0x49,0x49,0x29,0x1E},{0x00,0x36,0x36,0x00,0x00},
    {0x00,0x56,0x36,0x00,0x00},{0x08,0x14,0x22,0x41,0x00},{0x14,0x14,0x14,0x14,0x14},
    {0x00,0x41,0x22,0x14,0x08},{0x02,0x01,0x51,0x09,0x06},{0x32,0x49,0x79,0x41,0x3E},
    {0x7E,0x11,0x11,0x11,0x7E},{0x7F,0x49,0x49,0x49,0x36},{0x3E,0x41,0x41,0x41,0x22},
    {0x7F,0x41,0x41,0x22,0x1C},{0x7F,0x49,0x49,0x49,0x41},{0x7F,0x09,0x09,0x09,0x01},
    {0x3E,0x41,0x49,0x49,0x7A},{0x7F,0x08,0x08,0x08,0x7F},{0x00,0x41,0x7F,0x41,0x00},
    {0x20,0x40,0x41,0x3F,0x01},{0x7F,0x08,0x14,0x22,0x41},{0x7F,0x40,0x40,0x40,0x40},
    {0x7F,0x02,0x0C,0x02,0x7F},{0x7F,0x04,0x08,0x10,0x7F},{0x3E,0x41,0x41,0x41,0x3E},
    {0x7F,0x09,0x09,0x09,0x06},{0x3E,0x41,0x51,0x21,0x5E},{0x7F,0x09,0x19,0x29,0x46},
    {0x46,0x49,0x49,0x49,0x31},{0x01,0x01,0x7F,0x01,0x01},{0x3F,0x40,0x40,0x40,0x3F},
    {0x1F,0x20,0x40,0x20,0x1F},{0x3F,0x40,0x38,0x40,0x3F},{0x63,0x14,0x08,0x14,0x63},
    {0x07,0x08,0x70,0x08,0x07},{0x61,0x51,0x49,0x45,0x43},{0x00,0x7F,0x41,0x41,0x00},
    {0x02,0x04,0x08,0x10,0x20},{0x00,0x41,0x41,0x7F,0x00},{0x04,0x02,0x01,0x02,0x04},
    {0x40,0x40,0x40,0x40,0x40},{0x00,0x01,0x02,0x04,0x00},{0x20,0x54,0x54,0x54,0x78},
    {0x7F,0x48,0x44,0x44,0x38},{0x38,0x44,0x44,0x44,0x20},{0x38,0x44,0x44,0x48,0x7F},
    {0x38,0x54,0x54,0x54,0x18},{0x08,0x7E,0x09,0x01,0x02},{0x0C,0x52,0x52,0x52,0x3E},
    {0x7F,0x08,0x04,0x04,0x78},{0x00,0x44,0x7D,0x40,0x00},{0x20,0x40,0x44,0x3D,0x00},
    {0x7F,0x10,0x28,0x44,0x00},{0x00,0x41,0x7F,0x40,0x00},{0x7C,0x04,0x18,0x04,0x78},
    {0x7C,0x08,0x04,0x04,0x78},{0x38,0x44,0x44,0x44,0x38},{0x7C,0x14,0x14,0x14,0x08},
    {0x08,0x14,0x14,0x18,0x7C},{0x7C,0x08,0x04,0x04,0x08},{0x48,0x54,0x54,0x54,0x20},
    {0x04,0x3F,0x44,0x40,0x20},{0x3C,0x40,0x40,0x40,0x3C},{0x1C,0x20,0x40,0x20,0x1C},
    {0x3C,0x40,0x30,0x40,0x3C},{0x44,0x28,0x10,0x28,0x44},{0x0C,0x50,0x50,0x50,0x3C},
    {0x44,0x64,0x54,0x4C,0x44},{0x00,0x08,0x36,0x41,0x00},{0x00,0x00,0x7F,0x00,0x00},
    {0x00,0x41,0x36,0x08,0x00},{0x10,0x08,0x08,0x10,0x08},{0x78,0x46,0x41,0x46,0x78},
};

// Scrie un string pe OLED la pagina şi coloana date (6px per caracter).
// Zero-fillează restul liniei până la col 128 pentru a şterge conţinut vechi.
static void oled_write_str(uint8_t page, uint8_t col, const char *s)
{
    i2c_lock();
    mux_open(MUX_CH_OLED);
    oled_cmd((uint8_t)(0xB0 + page));
    oled_cmd((uint8_t)(col & 0x0F));
    oled_cmd((uint8_t)(0x10 | (col >> 4)));
    while (*s && col <= 122) {   // 122 = ultimul start valid (122+6=128)
        uint8_t c = (uint8_t)*s++;
        if (c < 0x20 || c > 0x7F) c = 0x20;
        const uint8_t *g = g_font5x7[c - 0x20];
        for (int i = 0; i < 5; i++) {
            uint8_t b[2] = { OLED_DATA_BYTE, g[i] };
            i2c_master_write_to_device(I2C_PORT, OLED_ADDR, b, 2, pdMS_TO_TICKS(I2C_TIMEOUT_MS));
        }
        uint8_t gap[2] = { OLED_DATA_BYTE, 0x00 };
        i2c_master_write_to_device(I2C_PORT, OLED_ADDR, gap, 2, pdMS_TO_TICKS(I2C_TIMEOUT_MS));
        col += 6;
    }
    // Şterge pixelii rămaşi din write-uri anterioare
    while (col < 128) {
        uint8_t b[2] = { OLED_DATA_BYTE, 0x00 };
        i2c_master_write_to_device(I2C_PORT, OLED_ADDR, b, 2, pdMS_TO_TICKS(I2C_TIMEOUT_MS));
        col++;
    }
    mux_close();
    i2c_unlock();
}

// Afişează ultimele date citite pe OLED (apelat la fiecare POST)
//   ds_read_ok = succesul ultimei citiri de temperatură
//   Pulsul (BPM) e produs de pulse_task şi citit din globala g_bpm.
static void oled_show_data(bool ds_read_ok, float ds_temp)
{
    if (!g_oled_ok) return;

    char line[32];

    i2c_lock();   // ţinem lock-ul peste tot draw-ul (mutex recursiv)

    // Page 0: titlu centrat
    oled_write_str(0, 34, "LifeAlert+");

    // Page 2: Puls (BPM real calculat de pulse_task)
    if (!g_max_ok) {
        snprintf(line, sizeof(line), "Puls: ERR");
    } else if (!g_finger_on) {
        snprintf(line, sizeof(line), "Puls: --");
    } else if (g_bpm > 0) {
        snprintf(line, sizeof(line), "Puls: %d bpm", g_bpm);
    } else {
        snprintf(line, sizeof(line), "Puls: ...");  // deget pus, încă se calculează
    }
    oled_write_str(2, 0, line);

    // Page 3: Temperatură
    if (ds_read_ok) {
        bool neg = (ds_temp < 0.0f);
        int ti = (int)(fabsf(ds_temp) * 10.0f + 0.5f);
        snprintf(line, sizeof(line), "Temp:%c%d.%dC",
                 neg ? '-' : ' ', ti / 10, ti % 10);
    } else {
        snprintf(line, sizeof(line), "Temp: --.-C");
    }
    oled_write_str(3, 0, line);

    // Page 4: Net + Sys
    snprintf(line, sizeof(line), "Net:%-3s  Sys:%s",
             s_wifi_ok ? "OK" : "OFF",
             g_sleep_mode ? "SLP" : "ON");
    oled_write_str(4, 0, line);

    // Page 5: Baterie + coadă (Bat = placeholder — ADC nelegat la VBAT)
    snprintf(line, sizeof(line), "Bat: --   Q:%lu",
             (unsigned long)s_q_count);
    oled_write_str(5, 0, line);

    // Page 7: stare globală — eroare doar dacă init la boot a eşuat sau wifi off
    if (!g_mpu_ok || !g_max_ok || !g_ds_ok) {
        snprintf(line, sizeof(line), "Err:%s%s%s",
                 g_mpu_ok ? "" : " MPU",
                 g_max_ok ? "" : " MAX",
                 g_ds_ok  ? "" : " DS");
    } else if (!s_wifi_ok) {
        snprintf(line, sizeof(line), "Stare: OFFLINE");
    } else {
        snprintf(line, sizeof(line), "Stare: OK");
    }
    oled_write_str(7, 0, line);

    i2c_unlock();
}

// Ecran afişat când sistemul intră în sleep mode (butonul 2)
static void oled_show_sleep(void)
{
    if (!g_oled_ok) return;
    i2c_lock();
    oled_clear();
    oled_write_str(0, 34, "LifeAlert+");
    oled_write_str(3, 40, "SISTEM");
    oled_write_str(4, 46, "OPRIT");
    oled_write_str(6, 4,  "Buton 2 = pornire");
    i2c_unlock();
}

// =============================================================================
// DS18B20 (1-Wire) — non-blocking: start conversion, read on the next cycle
// =============================================================================
static void ow_low(void)  { gpio_set_level((gpio_num_t)DS18B20_PIN, 0); }
static void ow_hi(void)   { gpio_set_level((gpio_num_t)DS18B20_PIN, 1); }
static int  ow_pin(void)  { return gpio_get_level((gpio_num_t)DS18B20_PIN); }

static bool ow_reset(void)
{
    for (int t = 0; t < 3; t++) {
        ow_low();  esp_rom_delay_us(500);
        ow_hi();   esp_rom_delay_us(80);
        bool p = (ow_pin() == 0);
        esp_rom_delay_us(420);
        if (p) return true;
        esp_rom_delay_us(2000);
    }
    return false;
}

static void ow_write_bit(int bit)
{
    if (bit) { ow_low(); esp_rom_delay_us(6);  ow_hi(); esp_rom_delay_us(64); }
    else     { ow_low(); esp_rom_delay_us(60); ow_hi(); esp_rom_delay_us(10); }
}

static int ow_read_bit(void)
{
    ow_low(); esp_rom_delay_us(6);
    ow_hi();  esp_rom_delay_us(9);
    int b = ow_pin();
    esp_rom_delay_us(55);
    return b;
}

static void ow_write_byte(uint8_t byte)
{
    for (int i = 0; i < 8; i++) { ow_write_bit(byte & 1); byte >>= 1; }
}

static uint8_t ow_read_byte(void)
{
    uint8_t b = 0;
    for (int i = 0; i < 8; i++) { b >>= 1; if (ow_read_bit()) b |= 0x80; }
    return b;
}

static uint8_t ds_crc8(const uint8_t *data, size_t len)
{
    uint8_t crc = 0;
    for (size_t i = 0; i < len; i++) {
        uint8_t v = data[i];
        for (int j = 0; j < 8; j++) {
            if ((crc ^ v) & 1) crc = (crc >> 1) ^ 0x8C;
            else               crc >>= 1;
            v >>= 1;
        }
    }
    return crc;
}

static void ds_gpio_init(void)
{
    gpio_config_t c = {};
    c.pin_bit_mask = (1ULL << DS18B20_PIN);
    c.mode         = GPIO_MODE_INPUT_OUTPUT_OD;
    c.pull_up_en   = GPIO_PULLUP_ENABLE;
    c.intr_type    = GPIO_INTR_DISABLE;
    gpio_config(&c);
    ow_hi();
}

static bool ds_start_conv(void)
{
    if (!ow_reset()) return false;
    ow_write_byte(0xCC);   // Skip ROM
    ow_write_byte(0x44);   // Convert T
    ow_hi();
    return true;
}

static bool ds_read_temp(float *out)
{
    uint8_t sp[9];
    ow_hi();
    if (!ow_reset()) return false;
    ow_write_byte(0xCC);
    ow_write_byte(0xBE);   // Read Scratchpad
    for (int i = 0; i < 9; i++) sp[i] = ow_read_byte();
    if (ds_crc8(sp, 8) != sp[8]) return false;
    bool all0 = true, allF = true;
    for (int i = 0; i < 9; i++) {
        if (sp[i] != 0x00) all0 = false;
        if (sp[i] != 0xFF) allF = false;
    }
    if (all0 || allF || (sp[1] == 0x05 && sp[0] == 0x50)) return false;
    *out = (float)(int16_t)((sp[1] << 8) | sp[0]) / 16.0f;
    return true;
}

// =============================================================================
// GPS NEO-6M
// =============================================================================
static bool nmea_cksum_ok(const char *line)
{
    if (line[0] != '$') return false;
    const char *star = strchr(line, '*');
    if (!star || strlen(star) < 3) return false;
    uint8_t sum = 0;
    for (const char *p = line + 1; p < star; p++) sum ^= (uint8_t)(*p);
    char hex[3] = { star[1], star[2], 0 };
    return sum == (uint8_t)strtoul(hex, NULL, 16);
}

static void nmea_field(const char *s, int n, char *out, int sz)
{
    out[0] = 0;
    int field = 0;
    const char *p = s;
    while (*p && field < n) { if (*p++ == ',') field++; }
    int i = 0;
    while (*p && *p != ',' && *p != '*' && *p != '\r' && *p != '\n' && i < sz - 1)
        out[i++] = *p++;
    out[i] = 0;
}

static float nmea_to_deg(const char *coord, char dir)
{
    if (!coord[0]) return 0.0f;
    float raw = (float)atof(coord);
    int   deg = (int)(raw / 100);
    float r   = (float)deg + (raw - (float)(deg * 100)) / 60.0f;
    return (dir == 'S' || dir == 'W') ? -r : r;
}

static void gps_parse(const char *line)
{
    if (!nmea_cksum_ok(line)) return;
    g_gps.has_nmea = true;
    char f[20];
    if (strncmp(line, "$GPRMC", 6) == 0 || strncmp(line, "$GNRMC", 6) == 0) {
        char time[16], status[4], lat[16], ns[4], lon[16], ew[4], spd[16], date[10];
        nmea_field(line, 1, time,   sizeof(time));
        nmea_field(line, 2, status, sizeof(status));
        nmea_field(line, 3, lat,    sizeof(lat));
        nmea_field(line, 4, ns,     sizeof(ns));
        nmea_field(line, 5, lon,    sizeof(lon));
        nmea_field(line, 6, ew,     sizeof(ew));
        nmea_field(line, 7, spd,    sizeof(spd));
        nmea_field(line, 9, date,   sizeof(date));
        g_gps.valid = (status[0] == 'A');
        if (g_gps.valid) {
            g_gps.lat       = nmea_to_deg(lat, ns[0]);
            g_gps.lon       = nmea_to_deg(lon, ew[0]);
            g_gps.speed_kmh = (float)atof(spd) * 1.852f;
        }
        if (strlen(time) >= 6)
            snprintf(g_gps.time_str, sizeof(g_gps.time_str), "%c%c:%c%c:%c%c",
                     time[0], time[1], time[2], time[3], time[4], time[5]);
        if (strlen(date) >= 6)
            snprintf(g_gps.date_str, sizeof(g_gps.date_str), "%c%c/%c%c/%c%c",
                     date[0], date[1], date[2], date[3], date[4], date[5]);
    } else if (strncmp(line, "$GPGGA", 6) == 0 || strncmp(line, "$GNGGA", 6) == 0) {
        char lat[16], ns[4], lon[16], ew[4];
        nmea_field(line, 2, lat, sizeof(lat));
        nmea_field(line, 3, ns,  sizeof(ns));
        nmea_field(line, 4, lon, sizeof(lon));
        nmea_field(line, 5, ew,  sizeof(ew));
        nmea_field(line, 6, f,   sizeof(f)); g_gps.fix_quality = atoi(f);
        nmea_field(line, 7, f,   sizeof(f)); g_gps.satellites  = atoi(f);
        if (g_gps.fix_quality > 0) {
            g_gps.valid = true;
            g_gps.lat   = nmea_to_deg(lat, ns[0]);
            g_gps.lon   = nmea_to_deg(lon, ew[0]);
        }
    }
}

static void gps_feed(const uint8_t *data, int len)
{
    for (int i = 0; i < len; i++) {
        char c = (char)data[i];
        if (c == '\n') {
            g_gps_line[g_gps_line_n] = 0;
            if (g_gps_line_n > 0 && g_gps_line[0] == '$') gps_parse(g_gps_line);
            g_gps_line_n = 0;
        } else if (c != '\r' && g_gps_line_n < (int)sizeof(g_gps_line) - 1) {
            g_gps_line[g_gps_line_n++] = c;
        }
    }
}

static void gps_setup(void)
{
    uart_config_t c = {};
    c.baud_rate = 9600;
    c.data_bits = UART_DATA_8_BITS;
    c.parity    = UART_PARITY_DISABLE;
    c.stop_bits = UART_STOP_BITS_1;
    c.flow_ctrl = UART_HW_FLOWCTRL_DISABLE;
    uart_param_config(GPS_UART_NUM, &c);
    uart_set_pin(GPS_UART_NUM, GPS_TX_PIN, GPS_RX_PIN, UART_PIN_NO_CHANGE, UART_PIN_NO_CHANGE);
    uart_driver_install(GPS_UART_NUM, GPS_RX_BUF, 0, 0, NULL, 0);
    uart_flush_input(GPS_UART_NUM);
}

// =============================================================================
// WiFi Provisioning — SoftAP + HTTP server
// Dacă nu există nicio rețea configurată în NVS, ESP pornește ca hotspot
// (fără parolă), servește un formular la 192.168.4.1 și salvează credențialele
// introduse de utilizator în NVS, după care se repornește normal.
// =============================================================================

// Pagina principală — formular SSID + parolă
static const char PROV_PAGE[] =
    "<!DOCTYPE html><html><head>"
    "<meta charset='utf-8'>"
    "<meta name='viewport' content='width=device-width,initial-scale=1'>"
    "<title>LifeAlertPlus</title>"
    "<style>"
    "body{font-family:sans-serif;max-width:380px;margin:32px auto;padding:0 16px;background:#f5f5f5;}"
    "h2{color:#1565c0;margin-bottom:4px;}p{color:#555;font-size:14px;}"
    "label{font-size:13px;color:#333;display:block;margin-top:12px;}"
    "input{width:100%;padding:10px;margin-top:4px;box-sizing:border-box;"
    "border:1px solid #ccc;border-radius:6px;font-size:15px;background:#fff;}"
    "button{width:100%;padding:13px;margin-top:20px;background:#1565c0;"
    "color:#fff;border:none;border-radius:6px;font-size:16px;cursor:pointer;}"
    "button:active{background:#0d47a1;}"
    ".card{background:#fff;border-radius:10px;padding:20px;box-shadow:0 2px 8px rgba(0,0,0,.1);}"
    "</style></head><body>"
    "<div class='card'>"
    "<h2>LifeAlertPlus</h2>"
    "<p>Introduceti datele retelei WiFi la care sa se conecteze dispozitivul.</p>"
    "<form method='POST' action='/save'>"
    "<label>Nume retea (SSID)</label>"
    "<input name='ssid' type='text' placeholder='WiFi_Acasa' required maxlength='32' autocomplete='off'>"
    "<label>Parola WiFi</label>"
    "<input name='pass' type='password' placeholder='(lasati gol daca nu are parola)' maxlength='64'>"
    "<button type='submit'>Salveaza si conecteaza</button>"
    "</form></div></body></html>";

// Pagina afișată după salvare
static const char PROV_DONE[] =
    "<!DOCTYPE html><html><head>"
    "<meta charset='utf-8'>"
    "<meta name='viewport' content='width=device-width,initial-scale=1'>"
    "<title>Salvat!</title>"
    "<style>body{font-family:sans-serif;max-width:380px;margin:60px auto;padding:0 16px;text-align:center;}"
    "h2{color:#2e7d32;}p{color:#555;font-size:14px;line-height:1.5;}"
    ".card{background:#fff;border-radius:10px;padding:30px;box-shadow:0 2px 8px rgba(0,0,0,.1);}"
    "</style></head><body>"
    "<div class='card'>"
    "<h2>&#10003; Salvat!</h2>"
    "<p>Dispozitivul se va reporni si va incerca sa se conecteze la reteaua configurata.</p>"
    "<p>Puteti deconecta telefonul de la hotspot-ul LifeAlertPlus.</p>"
    "</div></body></html>";

// Decodează URL-encoding in-place (ex: %20 → ' ', + → ' ')
static void url_decode(char *dst, const char *src, int dst_sz)
{
    int i = 0, j = 0;
    while (src[i] && j < dst_sz - 1) {
        if (src[i] == '%' && src[i+1] && src[i+2]) {
            char hex[3] = { src[i+1], src[i+2], 0 };
            dst[j++] = (char)strtol(hex, NULL, 16);
            i += 3;
        } else {
            dst[j++] = (src[i] == '+') ? ' ' : src[i];
            i++;
        }
    }
    dst[j] = 0;
}

// Extrage valoarea unui câmp dintr-un body URL-encoded (ex: "ssid=Acasa&pass=1234")
static bool form_field(const char *body, const char *key, char *out, int out_sz)
{
    char search[40];
    snprintf(search, sizeof(search), "%s=", key);
    const char *p = strstr(body, search);
    if (!p) { out[0] = 0; return false; }
    p += strlen(search);
    const char *end = strchr(p, '&');
    char raw[128] = {};
    int n = end ? (int)(end - p) : (int)strlen(p);
    if (n >= (int)sizeof(raw)) n = (int)sizeof(raw) - 1;
    memcpy(raw, p, n);
    url_decode(out, raw, out_sz);
    return true;
}

static esp_err_t prov_get_handler(httpd_req_t *req)
{
    httpd_resp_set_type(req, "text/html; charset=utf-8");
    httpd_resp_send(req, PROV_PAGE, HTTPD_RESP_USE_STRLEN);
    return ESP_OK;
}

static esp_err_t prov_save_handler(httpd_req_t *req)
{
    char body[256] = {};
    int len = httpd_req_recv(req, body, sizeof(body) - 1);
    if (len <= 0) { httpd_resp_send_500(req); return ESP_FAIL; }
    body[len] = 0;

    char ssid[33] = {}, pass[65] = {};
    form_field(body, "ssid", ssid, sizeof(ssid));
    form_field(body, "pass", pass, sizeof(pass));

    if (ssid[0] == 0) {
        httpd_resp_send_err(req, HTTPD_400_BAD_REQUEST, "SSID required");
        return ESP_FAIL;
    }

    nvs_handle_t h;
    if (nvs_open("wifi_cfg", NVS_READWRITE, &h) == ESP_OK) {
        nvs_set_u8(h,  "n",  1);
        nvs_set_str(h, "s0", ssid);
        nvs_set_str(h, "p0", pass);
        nvs_commit(h);
        nvs_close(h);
        printf("[PROV] Salvat: SSID='%s' — repornire...\n", ssid);
    }

    httpd_resp_set_type(req, "text/html; charset=utf-8");
    httpd_resp_send(req, PROV_DONE, HTTPD_RESP_USE_STRLEN);

    vTaskDelay(pdMS_TO_TICKS(1500));   // permite răspunsul să fie trimis
    esp_restart();
    return ESP_OK;
}

// Intră în modul de provisionare — nu se întoarce niciodată (esp_restart() la final)
static void provisioning_mode(void)
{
    // SSID hotspot: "LifeAlert-XXXXXX" (ultimele 6 caractere din serial)
    char ap_ssid[24];
    const char *suffix = g_serial + strlen(g_serial) - 6;
    snprintf(ap_ssid, sizeof(ap_ssid), "LifeAlert-%.6s", suffix);

    printf("\n[PROV] Nicio retea configurata — pornesc hotspot: \"%s\"\n", ap_ssid);
    printf("[PROV] Conectati-va la hotspot si accesati http://192.168.4.1\n");

    if (g_oled_ok) {
        oled_clear();
        oled_write_str(0, 10, "-- CONFIGURARE --");
        oled_write_str(2, 0,  "WiFi hotspot:");
        oled_write_str(3, 0,  ap_ssid);
        oled_write_str(5, 0,  "192.168.4.1");
        oled_write_str(7, 0,  "Conecteaza-te!");
    }

    // LED galben pornit fix în timpul provisionarii
    gpio_set_level((gpio_num_t)LED_GREEN_PIN,  0);
    gpio_set_level((gpio_num_t)LED_YELLOW_PIN, 1);

    // Inițializare WiFi în mod AP
    ESP_ERROR_CHECK(esp_netif_init());
    ESP_ERROR_CHECK(esp_event_loop_create_default());
    esp_netif_create_default_wifi_ap();

    wifi_init_config_t cfg = WIFI_INIT_CONFIG_DEFAULT();
    ESP_ERROR_CHECK(esp_wifi_init(&cfg));
    ESP_ERROR_CHECK(esp_wifi_set_mode(WIFI_MODE_AP));

    wifi_config_t ap_cfg = {};
    strncpy((char *)ap_cfg.ap.ssid, ap_ssid, sizeof(ap_cfg.ap.ssid) - 1);
    ap_cfg.ap.ssid_len       = (uint8_t)strlen(ap_ssid);
    ap_cfg.ap.channel        = 1;
    ap_cfg.ap.authmode       = WIFI_AUTH_OPEN;   // fără parolă pe hotspot
    ap_cfg.ap.max_connection = 4;

    ESP_ERROR_CHECK(esp_wifi_set_config(WIFI_IF_AP, &ap_cfg));
    ESP_ERROR_CHECK(esp_wifi_start());

    // Server HTTP pe portul 80
    httpd_handle_t server = NULL;
    httpd_config_t srv_cfg = HTTPD_DEFAULT_CONFIG();
    srv_cfg.lru_purge_enable = true;
    ESP_ERROR_CHECK(httpd_start(&server, &srv_cfg));

    httpd_uri_t get_uri  = { "/",     HTTP_GET,  prov_get_handler,  NULL };
    httpd_uri_t save_uri = { "/save", HTTP_POST, prov_save_handler, NULL };
    httpd_register_uri_handler(server, &get_uri);
    httpd_register_uri_handler(server, &save_uri);

    printf("[PROV] Server HTTP pornit. Astept credentiale...\n");

    // Clipire LED galben cât așteptăm (prov_save_handler face esp_restart)
    while (true) {
        gpio_set_level((gpio_num_t)LED_YELLOW_PIN, 1);
        vTaskDelay(pdMS_TO_TICKS(600));
        gpio_set_level((gpio_num_t)LED_YELLOW_PIN, 0);
        vTaskDelay(pdMS_TO_TICKS(600));
    }
}

// =============================================================================
// WiFi
// =============================================================================
static void on_wifi_event(void *, esp_event_base_t base, int32_t id, void *data)
{
    if (base == WIFI_EVENT && id == WIFI_EVENT_STA_START) {
        esp_wifi_connect();
    } else if (base == WIFI_EVENT && id == WIFI_EVENT_STA_DISCONNECTED) {
        s_wifi_ok = false;
        gpio_set_level((gpio_num_t)LED_YELLOW_PIN, 1);  // yellow on = offline
        if (s_wifi_retry < WIFI_MAX_RETRY) {
            esp_wifi_connect();
            printf("[WiFi] Reconnecting (%d/%d)...\n", ++s_wifi_retry, WIFI_MAX_RETRY);
        } else {
            xEventGroupSetBits(s_wifi_eg, WIFI_FAIL_BIT);
        }
    } else if (base == IP_EVENT && id == IP_EVENT_STA_GOT_IP) {
        ip_event_got_ip_t *e = (ip_event_got_ip_t *)data;
        printf("[WiFi] IP: " IPSTR "\n", IP2STR(&e->ip_info.ip));
        s_wifi_retry = 0;
        s_wifi_ok    = true;
        // Setează 8.8.8.8 direct în tabelul LWIP (index 1 = backup).
        // esp_netif_set_dns_info nu propagă fiabil la LWIP când DHCP e activ.
        const ip_addr_t dns_8888 = IPADDR4_INIT_BYTES(8, 8, 8, 8);
        dns_setserver(1, &dns_8888);
        xEventGroupSetBits(s_wifi_eg, WIFI_OK_BIT);
    }
}

// Load up to 3 WiFi networks from NVS namespace "wifi_cfg".
// Falls back to compile-time defaults if nothing is stored.
static uint8_t s_wifi_net_count = 0;
static char    s_wifi_ssids[3][33];
static char    s_wifi_passes[3][65];

static void wifi_load_credentials(void)
{
    nvs_handle_t h;
    if (nvs_open("wifi_cfg", NVS_READONLY, &h) == ESP_OK) {
        uint8_t n = 0;
        nvs_get_u8(h, "n", &n);
        s_wifi_net_count = 0;
        for (uint8_t i = 0; i < n && i < 3; i++) {
            char sk[8], pk[8];
            snprintf(sk, sizeof(sk), "s%u", i);
            snprintf(pk, sizeof(pk), "p%u", i);
            size_t sl = sizeof(s_wifi_ssids[i]);
            size_t pl = sizeof(s_wifi_passes[i]);
            if (nvs_get_str(h, sk, s_wifi_ssids[i], &sl) == ESP_OK &&
                nvs_get_str(h, pk, s_wifi_passes[i], &pl) == ESP_OK)
                s_wifi_net_count++;
        }
        nvs_close(h);
    }
    if (s_wifi_net_count == 0 && WIFI_SSID_DEFAULT[0] != '\0') {
        // No NVS entries — fall back to the compile-time default if set.
        strncpy(s_wifi_ssids[0],  WIFI_SSID_DEFAULT, sizeof(s_wifi_ssids[0]) - 1);
        strncpy(s_wifi_passes[0], WIFI_PASS_DEFAULT, sizeof(s_wifi_passes[0]) - 1);
        s_wifi_net_count = 1;
    }
    if (s_wifi_net_count == 0) {
        printf("[WiFi] No networks configured. Provision one from the web UI; running offline until then.\n");
    } else {
        printf("[WiFi] %u network(s) configured\n", s_wifi_net_count);
    }
}

static bool wifi_try_connect(uint8_t idx)
{
    s_wifi_retry = 0;
    xEventGroupClearBits(s_wifi_eg, WIFI_OK_BIT | WIFI_FAIL_BIT);

    wifi_config_t wc = {};
    strncpy((char *)wc.sta.ssid,     s_wifi_ssids[idx],  sizeof(wc.sta.ssid)  - 1);
    strncpy((char *)wc.sta.password, s_wifi_passes[idx], sizeof(wc.sta.password) - 1);
    // Rețelele fără parolă (open) nu trec de WPA2 — folosim WPA doar dacă există parolă
    wc.sta.threshold.authmode = (s_wifi_passes[idx][0] != '\0') ? WIFI_AUTH_WPA2_PSK : WIFI_AUTH_OPEN;

    esp_wifi_disconnect();
    esp_wifi_set_config(WIFI_IF_STA, &wc);
    esp_wifi_connect();

    printf("[WiFi] Trying \"%s\"...\n", s_wifi_ssids[idx]);
    EventBits_t bits = xEventGroupWaitBits(
        s_wifi_eg, WIFI_OK_BIT | WIFI_FAIL_BIT, pdFALSE, pdFALSE, pdMS_TO_TICKS(12000));
    return (bits & WIFI_OK_BIT) != 0;
}

static bool wifi_setup(void)
{
    s_wifi_eg = xEventGroupCreate();

    esp_err_t ret = nvs_flash_init();
    if (ret == ESP_ERR_NVS_NO_FREE_PAGES || ret == ESP_ERR_NVS_NEW_VERSION_FOUND) {
        nvs_flash_erase();
        ret = nvs_flash_init();
    }
    ESP_ERROR_CHECK(ret);

    wifi_load_credentials();

    // Nicio rețea configurată și nicio valoare implicită la compilare → provisioning mode.
    // provisioning_mode() nu se întoarce: servește formularul, salvează în NVS, face restart.
    if (s_wifi_net_count == 0) {
        provisioning_mode();
    }

    ESP_ERROR_CHECK(esp_netif_init());
    ESP_ERROR_CHECK(esp_event_loop_create_default());
    s_wifi_netif = esp_netif_create_default_wifi_sta();

    wifi_init_config_t cfg = WIFI_INIT_CONFIG_DEFAULT();
    ESP_ERROR_CHECK(esp_wifi_init(&cfg));

    esp_event_handler_instance_register(WIFI_EVENT, ESP_EVENT_ANY_ID,    on_wifi_event, NULL, NULL);
    esp_event_handler_instance_register(IP_EVENT,   IP_EVENT_STA_GOT_IP, on_wifi_event, NULL, NULL);

    ESP_ERROR_CHECK(esp_wifi_set_mode(WIFI_MODE_STA));
    ESP_ERROR_CHECK(esp_wifi_start());

    // Try each stored network in sequence
    for (uint8_t i = 0; i < s_wifi_net_count; i++) {
        if (wifi_try_connect(i)) return true;
        printf("[WiFi] \"%s\" failed, trying next...\n", s_wifi_ssids[i]);
    }
    return false;
}

// =============================================================================
// HTTP POST — generic helper
// =============================================================================
static bool http_post_to(const char *path, const char *json)
{
    if (!s_wifi_ok) return false;

    char url[128];
    snprintf(url, sizeof(url), "%s%s", API_BASE_URL, path);

    esp_http_client_config_t cfg = {};
    cfg.url               = url;
    cfg.method            = HTTP_METHOD_POST;
    cfg.timeout_ms        = 15000;
    cfg.crt_bundle_attach = esp_crt_bundle_attach;

    esp_http_client_handle_t client = esp_http_client_init(&cfg);
    if (!client) return false;

    esp_http_client_set_header(client, "Content-Type", "application/json");
    esp_http_client_set_header(client, "X-Device-Key", API_DEVICE_KEY);
    esp_http_client_set_post_field(client, json, (int)strlen(json));

    esp_err_t err = esp_http_client_perform(client);
    bool ok = false;
    if (err == ESP_OK || err == ESP_ERR_NOT_SUPPORTED) {
        // ESP_ERR_NOT_SUPPORTED is returned when the server sends 401 with a
        // non-Basic/Digest WWW-Authenticate scheme; the status code is still valid.
        int code = esp_http_client_get_status_code(client);
        ok = (code == 200);
        if (!ok) printf("[HTTP] %s → %d\n", path, code);
    } else {
        printf("[HTTP] %s → %s\n", path, esp_err_to_name(err));
    }
    esp_http_client_cleanup(client);
    return ok;
}

static bool http_post(const char *json) { return http_post_to(API_INGEST_PATH, json); }

// =============================================================================
// HTTP GET — captures response body into a caller-provided buffer via event cb.
// Used for fetching the WiFi config from the API.
// =============================================================================
typedef struct {
    char *buf;
    int   capacity;
    int   length;
} http_resp_t;

static esp_err_t _http_resp_evt(esp_http_client_event_t *evt)
{
    if (evt->event_id != HTTP_EVENT_ON_DATA) return ESP_OK;
    http_resp_t *r = (http_resp_t *)evt->user_data;
    if (!r || !r->buf || r->capacity <= 0) return ESP_OK;
    int n = evt->data_len;
    int room = r->capacity - r->length - 1;  // leave 1 for NUL
    if (n > room) n = room;
    if (n > 0) {
        memcpy(r->buf + r->length, evt->data, n);
        r->length += n;
        r->buf[r->length] = 0;
    }
    return ESP_OK;
}

static bool http_get_to(const char *path, char *out, int out_sz, int *out_status)
{
    if (!s_wifi_ok || !out || out_sz <= 0) return false;
    out[0] = 0;
    if (out_status) *out_status = 0;

    char url[160];
    snprintf(url, sizeof(url), "%s%s", API_BASE_URL, path);

    http_resp_t r = { out, out_sz, 0 };

    esp_http_client_config_t cfg = {};
    cfg.url               = url;
    cfg.method            = HTTP_METHOD_GET;
    cfg.timeout_ms        = 15000;
    cfg.crt_bundle_attach = esp_crt_bundle_attach;
    cfg.event_handler     = _http_resp_evt;
    cfg.user_data         = &r;

    esp_http_client_handle_t client = esp_http_client_init(&cfg);
    if (!client) return false;

    esp_http_client_set_header(client, "X-Device-Key", API_DEVICE_KEY);

    esp_err_t err = esp_http_client_perform(client);
    bool ok = false;
    if (err == ESP_OK || err == ESP_ERR_NOT_SUPPORTED) {
        int code = esp_http_client_get_status_code(client);
        if (out_status) *out_status = code;
        ok = (code == 200);
    } else {
        printf("[HTTP-GET] %s → %s\n", path, esp_err_to_name(err));
    }
    esp_http_client_cleanup(client);
    return ok;
}

// =============================================================================
// Offline queue — NVS ring buffer, max QUEUE_MAX measurements
// =============================================================================
static nvs_handle_t s_q_nvs = 0;
// s_q_head / s_q_tail / s_q_count declared in global state section

static void queue_init(void)
{
    esp_err_t e = nvs_open(QUEUE_NS, NVS_READWRITE, &s_q_nvs);
    if (e != ESP_OK) { printf("[Q] NVS open failed\n"); return; }
    nvs_get_u32(s_q_nvs, "head",  &s_q_head);
    nvs_get_u32(s_q_nvs, "tail",  &s_q_tail);
    nvs_get_u32(s_q_nvs, "count", &s_q_count);
    printf("[Q] Loaded: %lu queued measurements\n", (unsigned long)s_q_count);
}

static void queue_push(const char *json)
{
    if (!s_q_nvs || s_q_count >= QUEUE_MAX) {
        if (s_q_count >= QUEUE_MAX) printf("[Q] Full — dropping oldest\n");
        // Drop oldest to make room
        s_q_head = (s_q_head + 1) % QUEUE_MAX;
        s_q_count--;
    }
    char key[8];
    snprintf(key, sizeof(key), "%lu", (unsigned long)s_q_tail);
    nvs_set_str(s_q_nvs, key, json);
    s_q_tail = (s_q_tail + 1) % QUEUE_MAX;
    s_q_count++;
    nvs_set_u32(s_q_nvs, "head",  s_q_head);
    nvs_set_u32(s_q_nvs, "tail",  s_q_tail);
    nvs_set_u32(s_q_nvs, "count", s_q_count);
    nvs_commit(s_q_nvs);
    printf("[Q] Enqueued (%lu total)\n", (unsigned long)s_q_count);
}

static void queue_flush(void)
{
    if (!s_q_nvs || s_q_count == 0) return;
    printf("[Q] Flushing %lu measurements (%d per ciclu)...\n",
           (unsigned long)s_q_count, QUEUE_FLUSH_PER_CYCLE);
    int sent = 0;
    while (s_q_count > 0 && s_wifi_ok && sent < QUEUE_FLUSH_PER_CYCLE) {
        char key[8];
        snprintf(key, sizeof(key), "%lu", (unsigned long)s_q_head);
        char json[512] = {};
        size_t len = sizeof(json);
        if (nvs_get_str(s_q_nvs, key, json, &len) != ESP_OK) {
            s_q_head = (s_q_head + 1) % QUEUE_MAX;
            s_q_count--;
            continue;
        }
        if (!http_post_to(API_INGEST_PATH, json)) break;
        nvs_erase_key(s_q_nvs, key);
        s_q_head = (s_q_head + 1) % QUEUE_MAX;
        s_q_count--;
        nvs_set_u32(s_q_nvs, "head",  s_q_head);
        nvs_set_u32(s_q_nvs, "count", s_q_count);
        nvs_commit(s_q_nvs);
        sent++;
    }
    if (s_q_count == 0) printf("[Q] Flush complet\n");
}

// =============================================================================
// Panic offline queue — coadă separată, prioritate la flush
// =============================================================================
static nvs_handle_t s_pq_nvs = 0;

static void panic_queue_init(void)
{
    esp_err_t e = nvs_open(PANIC_QUEUE_NS, NVS_READWRITE, &s_pq_nvs);
    if (e != ESP_OK) { printf("[PQ] NVS open failed\n"); return; }
    nvs_get_u32(s_pq_nvs, "head",  &s_pq_head);
    nvs_get_u32(s_pq_nvs, "tail",  &s_pq_tail);
    nvs_get_u32(s_pq_nvs, "count", &s_pq_count);
    if (s_pq_count > 0)
        printf("[PQ] %lu panic(s) pending from previous session\n",
               (unsigned long)s_pq_count);
}

static void panic_queue_push(const char *json)
{
    if (!s_pq_nvs) return;
    if (s_pq_count >= PANIC_QUEUE_MAX) {
        printf("[PQ] Full — dropping oldest panic\n");
        s_pq_head = (s_pq_head + 1) % PANIC_QUEUE_MAX;
        s_pq_count--;
    }
    char key[8];
    snprintf(key, sizeof(key), "%lu", (unsigned long)s_pq_tail);
    nvs_set_str(s_pq_nvs, key, json);
    s_pq_tail = (s_pq_tail + 1) % PANIC_QUEUE_MAX;
    s_pq_count++;
    nvs_set_u32(s_pq_nvs, "head",  s_pq_head);
    nvs_set_u32(s_pq_nvs, "tail",  s_pq_tail);
    nvs_set_u32(s_pq_nvs, "count", s_pq_count);
    nvs_commit(s_pq_nvs);
    printf("[PQ] Panic enqueued (%lu in coadă)\n", (unsigned long)s_pq_count);
}

static void panic_queue_flush(void)
{
    if (!s_pq_nvs || s_pq_count == 0 || !s_wifi_ok) return;
    printf("[PQ] Retrimitere %lu panic(uri) salvate...\n", (unsigned long)s_pq_count);
    while (s_pq_count > 0 && s_wifi_ok) {
        char key[8];
        snprintf(key, sizeof(key), "%lu", (unsigned long)s_pq_head);
        char json[256] = {};
        size_t len = sizeof(json);
        if (nvs_get_str(s_pq_nvs, key, json, &len) != ESP_OK) {
            s_pq_head = (s_pq_head + 1) % PANIC_QUEUE_MAX;
            s_pq_count--;
            continue;
        }
        if (!http_post_to(API_PANIC_PATH, json)) break;   // server încă jos — oprire
        nvs_erase_key(s_pq_nvs, key);
        s_pq_head = (s_pq_head + 1) % PANIC_QUEUE_MAX;
        s_pq_count--;
        nvs_set_u32(s_pq_nvs, "head",  s_pq_head);
        nvs_set_u32(s_pq_nvs, "count", s_pq_count);
        nvs_commit(s_pq_nvs);
        printf("[PQ] Panic retrimis OK (%lu rămase)\n", (unsigned long)s_pq_count);
    }
}

// =============================================================================
// WiFi config sync — pulls latest networks from API and persists to NVS.
// Networks are picked up by wifi_load_credentials() on next boot.
// =============================================================================
static void wifi_config_fetch_and_store(void)
{
    if (!s_wifi_ok) return;

    char path[96];
    snprintf(path, sizeof(path), "%s%s", API_WIFI_CONFIG_PATH, g_serial);

    char body[1024];
    int  status = 0;
    if (!http_get_to(path, body, sizeof(body), &status)) {
        if (status > 0) printf("[WIFI-CFG] fetch failed: HTTP %d\n", status);
        return;
    }

    cJSON *root = cJSON_Parse(body);
    if (!root) { printf("[WIFI-CFG] JSON parse failed\n"); return; }

    cJSON *nets = cJSON_GetObjectItem(root, "networks");
    if (!cJSON_IsArray(nets)) { cJSON_Delete(root); return; }

    char ssids[3][33]  = {};
    char passes[3][65] = {};
    int  valid = 0;
    cJSON *n = NULL;
    cJSON_ArrayForEach(n, nets) {
        if (valid >= 3) break;
        cJSON *s = cJSON_GetObjectItem(n, "ssid");
        cJSON *p = cJSON_GetObjectItem(n, "password");
        if (!cJSON_IsString(s) || !s->valuestring || s->valuestring[0] == 0) continue;
        strncpy(ssids[valid], s->valuestring, sizeof(ssids[valid]) - 1);
        if (cJSON_IsString(p) && p->valuestring)
            strncpy(passes[valid], p->valuestring, sizeof(passes[valid]) - 1);
        valid++;
    }
    cJSON_Delete(root);

    // Skip the NVS write if config is unchanged — avoids flash wear and log noise.
    bool same = (valid == s_wifi_net_count);
    if (same) {
        for (int i = 0; i < valid; i++) {
            if (strcmp(ssids[i],  s_wifi_ssids[i])  != 0 ||
                strcmp(passes[i], s_wifi_passes[i]) != 0) { same = false; break; }
        }
    }
    if (same) return;

    nvs_handle_t h;
    if (nvs_open("wifi_cfg", NVS_READWRITE, &h) != ESP_OK) {
        printf("[WIFI-CFG] NVS open failed\n");
        return;
    }
    nvs_set_u8(h, "n", (uint8_t)valid);
    for (int i = 0; i < valid; i++) {
        char sk[8], pk[8];
        snprintf(sk, sizeof(sk), "s%d", i);
        snprintf(pk, sizeof(pk), "p%d", i);
        nvs_set_str(h, sk, ssids[i]);
        nvs_set_str(h, pk, passes[i]);
    }
    nvs_commit(h);
    nvs_close(h);
    printf("[WIFI-CFG] updated NVS with %d network(s) — active on next reboot\n", valid);
}

// =============================================================================
// Heartbeat
// =============================================================================

// Callback al timer-ului — la 5s după intrarea în sleep, stinge LED galben + OLED.
// Verifică g_sleep_mode pentru a evita stingerea dacă userul a trezit între timp.
static void sleep_off_cb(void *)
{
    if (!g_sleep_mode) return;  // user a apăsat butonul 2 între timp — anulat
    gpio_set_level((gpio_num_t)LED_YELLOW_PIN, 0);
    if (g_oled_ok) {
        i2c_lock();
        mux_open(MUX_CH_OLED);
        oled_cmd(0xAE);   // SSD1306 display off
        mux_close();
        i2c_unlock();
    }
    printf("[SLP] LED galben + OLED stinse (5s după intrarea în sleep)\n");
}

static void heartbeat_send(void *)
{
    if (!s_wifi_ok || g_sleep_mode) return;
    int rssi = 0;
    esp_wifi_sta_get_rssi(&rssi);

    char json[256];
    snprintf(json, sizeof(json),
        "{\"serial\":\"%s\",\"rssiDbm\":%d,\"freeHeapBytes\":%lu,"
        "\"uptimeSeconds\":%llu,\"queuedMeasurements\":%lu,"
        "\"batteryVoltage\":0.0,\"sensorFlags\":15,"
        "\"firmwareVersion\":\"%s\"}",
        g_serial, rssi,
        (unsigned long)esp_get_free_heap_size(),
        (unsigned long long)(xTaskGetTickCount() * portTICK_PERIOD_MS / 1000ULL),
        (unsigned long)s_q_count,
        FIRMWARE_VERSION);

    http_post_to(API_HEARTBEAT_PATH, json);
    printf("[HB] RSSI=%d heap=%lu queue=%lu\n",
           rssi, (unsigned long)esp_get_free_heap_size(), (unsigned long)s_q_count);

    // Pull any updated WiFi networks configured from the web app.
    // Diff against the currently loaded set; writes NVS only when something changed.
    wifi_config_fetch_and_store();
}

// =============================================================================
// Panic alert
// =============================================================================
static void panic_send(void)
{
    char json[128];
    const char *coords = (g_gps.valid)
        ? (snprintf(json, sizeof(json),
               "{\"serial\":\"%s\",\"coordinates\":\"%.6f,%.6f\"}",
               g_serial, g_gps.lat, g_gps.lon), json)
        : (snprintf(json, sizeof(json),
               "{\"serial\":\"%s\",\"coordinates\":null}", g_serial), json);
    (void)coords;
    if (http_post_to(API_PANIC_PATH, json)) {
        printf("[BTN] Panic sent\n");
    } else {
        printf("[BTN] Panic send FAILED — salvat offline pentru retrimitere\n");
        panic_queue_push(json);
    }
}

// =============================================================================
// Buttons
// =============================================================================
static void IRAM_ATTR btn_isr(void *arg)
{
    int pin = (int)(intptr_t)arg;
    int idx = (pin == BUTTON1_PIN) ? 0 : 1;
    if (s_btn_disabled[idx]) return;               // stuck-LOW guard
    int64_t now = esp_timer_get_time();
    if (now - s_btn_last_us[idx] < 50000) return;  // 50 ms debounce
    s_btn_last_us[idx] = now;
    xQueueSendFromISR(s_btn_q, &pin, NULL);
}

static void btn_task(void *)
{
    int pin;
    int last_b1 = 1, last_b2 = 1;   // pull-up: idle HIGH
    while (true) {
        // ISR path: aşteaptă eveniment 30 ms; dacă nu vine, fă poll fallback
        bool got = xQueueReceive(s_btn_q, &pin, pdMS_TO_TICKS(30));

        if (!got) {
            // Polling — prinde apăsarea dacă ISR-ul nu se declanşează
            int b1 = gpio_get_level((gpio_num_t)BUTTON1_PIN);
            int b2 = gpio_get_level((gpio_num_t)BUTTON2_PIN);
            int64_t now_us = esp_timer_get_time();
            if (!s_btn_disabled[0] && last_b1 == 1 && b1 == 0 &&
                (now_us - s_btn_last_us[0]) > 50000) {
                s_btn_last_us[0] = now_us;
                pin = BUTTON1_PIN;
                got = true;
                printf("[BTN] GPIO%d falling edge (poll)\n", BUTTON1_PIN);
            } else if (!s_btn_disabled[1] && last_b2 == 1 && b2 == 0 &&
                       (now_us - s_btn_last_us[1]) > 50000) {
                s_btn_last_us[1] = now_us;
                pin = BUTTON2_PIN;
                got = true;
                printf("[BTN] GPIO%d falling edge (poll)\n", BUTTON2_PIN);
            }
            last_b1 = b1;
            last_b2 = b2;
        }

        if (!got) continue;

        {
                    if (pin == BUTTON1_PIN) {
                    printf("[BTN] Panic!\n");
                    for (int i = 0; i < 2; i++) {
                        gpio_set_level((gpio_num_t)LED_YELLOW_PIN, 1);
                        vTaskDelay(pdMS_TO_TICKS(150));
                        gpio_set_level((gpio_num_t)LED_YELLOW_PIN, 0);
                        vTaskDelay(pdMS_TO_TICKS(100));
                    }
                    panic_send();
                } else {
                    g_sleep_mode = !g_sleep_mode;
                    if (g_sleep_mode) {
                        // === SLEEP MODE ON ===
                        gpio_set_level((gpio_num_t)LED_GREEN_PIN,  0);
                        vTaskDelay(pdMS_TO_TICKS(50));
                        max_sleep();                                    // stinge LED MAX30100
                        vTaskDelay(pdMS_TO_TICKS(50));
                        esp_wifi_set_ps(WIFI_PS_MAX_MODEM);
                        vTaskDelay(pdMS_TO_TICKS(50));
                        gpio_set_level((gpio_num_t)LED_YELLOW_PIN, 1); // galben on (5s)
                        oled_show_sleep();                              // ecran "SISTEM OPRIT" (5s)
                        // Programează stingerea LED galben + OLED display după 5s
                        if (s_sleep_off_timer) {
                            esp_timer_stop(s_sleep_off_timer);  // ignorat dacă nu rulează
                            esp_timer_start_once(s_sleep_off_timer,
                                                 (uint64_t)SLEEP_OFF_DELAY_MS * 1000ULL);
                        }
                        printf("[BTN] >>> SISTEM OPRIT (sleep) — stingere LED+OLED în %dms\n",
                               SLEEP_OFF_DELAY_MS);
                    } else {
                        // === SLEEP MODE OFF ===
                        // Anulează timer-ul de stingere (poate să fi fost deja stins LED+OLED)
                        if (s_sleep_off_timer) esp_timer_stop(s_sleep_off_timer);
                        // Reaprinde OLED display (cazul în care timer-ul l-a stins după 5s)
                        if (g_oled_ok) {
                            i2c_lock();
                            mux_open(MUX_CH_OLED);
                            oled_cmd(0xAF);   // SSD1306 display on
                            mux_close();
                            i2c_unlock();
                        }
                        gpio_set_level((gpio_num_t)LED_YELLOW_PIN, 0);
                        vTaskDelay(pdMS_TO_TICKS(50));
                        esp_wifi_set_ps(WIFI_PS_NONE);
                        vTaskDelay(pdMS_TO_TICKS(50));
                        max_wake();
                        vTaskDelay(pdMS_TO_TICKS(50));
                        gpio_set_level((gpio_num_t)LED_GREEN_PIN,  1);
                        // Reset OLED — main loop va popula datele la următorul POST
                        if (g_oled_ok) { oled_clear(); oled_show_title(); }
                        printf("[BTN] >>> SISTEM PORNIT — POST activ la %d ms\n", API_POST_MS);
                    }
                }
        }
    }
}

static void buttons_setup(void)
{
    s_btn_q = xQueueCreate(10, sizeof(int));
    gpio_config_t c = {};
    c.pin_bit_mask = (1ULL << BUTTON1_PIN) | (1ULL << BUTTON2_PIN);
    c.mode         = GPIO_MODE_INPUT;
    c.pull_up_en   = GPIO_PULLUP_ENABLE;
    c.intr_type    = GPIO_INTR_NEGEDGE;
    gpio_config(&c);

    // Stuck-LOW guard: dacă un pin stă LOW continuu 500ms după setup,
    // butonul/cablajul e defect (scurt la GND) — dezactivează-l ca să nu
    // genereze false-presses continue.
    vTaskDelay(pdMS_TO_TICKS(20));  // stabilizare pull-up
    int low_count[2] = {0, 0};
    for (int i = 0; i < 25; i++) {  // 25 × 20ms = 500ms
        if (gpio_get_level((gpio_num_t)BUTTON1_PIN) == 0) low_count[0]++;
        if (gpio_get_level((gpio_num_t)BUTTON2_PIN) == 0) low_count[1]++;
        vTaskDelay(pdMS_TO_TICKS(20));
    }
    if (low_count[0] >= 24) {
        s_btn_disabled[0] = true;
        printf("[BTN] GPIO%d (panic) stuck LOW => DEZACTIVAT (cablaj/buton defect)\n", BUTTON1_PIN);
    }
    if (low_count[1] >= 24) {
        s_btn_disabled[1] = true;
        printf("[BTN] GPIO%d (sleep) stuck LOW => DEZACTIVAT (cablaj/buton defect)\n", BUTTON2_PIN);
    }

    // 8KB stack — panic_send() face HTTPS (mbedTLS) care consumă ~6KB
    xTaskCreate(btn_task, "btn", 8192, NULL, 5, NULL);
    gpio_isr_handler_add((gpio_num_t)BUTTON1_PIN, btn_isr, (void *)(intptr_t)BUTTON1_PIN);
    gpio_isr_handler_add((gpio_num_t)BUTTON2_PIN, btn_isr, (void *)(intptr_t)BUTTON2_PIN);
}

#if HW_SELFTEST
// Diagnostic la boot: verifică LED verde (GPIO 6) şi nivelul butoanelor (GPIO 3/4).
static void hw_selftest(void)
{
    printf("\n[DIAG] ===== HARDWARE SELF-TEST =====\n");

    // LED verde — clipeşte ambele polarităţi; dacă LED-ul e bun, se vede
    printf("[DIAG] LED verde GPIO%d: 12 clipiri — urmăreşte LED-ul...\n", LED_GREEN_PIN);
    for (int i = 0; i < 12; i++) {
        gpio_set_level((gpio_num_t)LED_GREEN_PIN, i & 1);
        vTaskDelay(pdMS_TO_TICKS(350));
    }
    gpio_set_level((gpio_num_t)LED_GREEN_PIN, 1);
    printf("[DIAG] Nu a clipit deloc => LED/rezistor/fir defect pe GPIO%d\n", LED_GREEN_PIN);

    // Butoane — citeşte nivelul brut 10s; idle=1 (pull-up), apăsat=0
    printf("[DIAG] Apasă AMBELE butoane în următoarele 10s...\n");
    int last1 = -1, last2 = -1;
    int press1_count = 0, press2_count = 0;   // contor de tranziţii 1->0
    int idle1_seen = 0,   idle2_seen = 0;     // confirmă pull-up funcţional
    for (int t = 0; t < 100; t++) {
        int l1 = gpio_get_level((gpio_num_t)BUTTON1_PIN);
        int l2 = gpio_get_level((gpio_num_t)BUTTON2_PIN);
        if (l1 == 1) idle1_seen++;
        if (l2 == 1) idle2_seen++;
        if (l1 != last1) {
            printf("[DIAG]  buton panică GPIO%d = %d\n", BUTTON1_PIN, l1);
            if (last1 == 1 && l1 == 0) press1_count++;
            last1 = l1;
        }
        if (l2 != last2) {
            printf("[DIAG]  buton sleep  GPIO%d = %d\n", BUTTON2_PIN, l2);
            if (last2 == 1 && l2 == 0) press2_count++;
            last2 = l2;
        }
        vTaskDelay(pdMS_TO_TICKS(100));
    }

    // Verdict final
    printf("[DIAG] ----- Rezultat butoane -----\n");
    if (idle1_seen == 0)
        printf("[DIAG]  GPIO%d (panică): NICIODATĂ HIGH => pull-up rupt / pin scurtcircuitat la GND\n", BUTTON1_PIN);
    else if (press1_count == 0)
        printf("[DIAG]  GPIO%d (panică): %d apăsări detectate (rămas la idle HIGH)\n", BUTTON1_PIN, press1_count);
    else
        printf("[DIAG]  GPIO%d (panică): OK, %d apăsări detectate\n", BUTTON1_PIN, press1_count);

    if (idle2_seen == 0)
        printf("[DIAG]  GPIO%d (sleep) : NICIODATĂ HIGH => pull-up rupt / pin scurtcircuitat la GND\n", BUTTON2_PIN);
    else if (press2_count == 0)
        printf("[DIAG]  GPIO%d (sleep) : NICIODATĂ LOW => buton/fir GND defect sau buton neapăsat\n", BUTTON2_PIN);
    else
        printf("[DIAG]  GPIO%d (sleep) : OK, %d apăsări detectate\n", BUTTON2_PIN, press2_count);
    printf("[DIAG] ===== END SELF-TEST =====\n\n");
}
#endif

// =============================================================================
// Sensor functional self-test — verifică prezență + valori sensibile
// =============================================================================
static void sensors_self_test(bool mpu_present, bool max_present,
                              bool oled_present, bool mux_present)
{
    printf("\n[SENS] ===== SENSOR FUNCTIONAL TEST =====\n");

    // MUX
    printf("[SENS] TCA9548 mux: %s (addr 0x%02X)\n",
           mux_present ? "PRESENT" : "MISSING", g_mux_addr);

    // MPU6050 — citeşte un sample şi verifică valori sensibile
    if (mpu_present) {
        int16_t a[3] = {}, g[3] = {};
        bool ok = mpu_read(a, g);
        bool stuck = ok && (a[0] == 0 && a[1] == 0 && a[2] == 0) &&
                            (g[0] == 0 && g[1] == 0 && g[2] == 0);
        bool railed = ok && ((uint16_t)a[0] == 0xFFFF && (uint16_t)a[1] == 0xFFFF);
        printf("[SENS] MPU6050:  %s | accel=[%d,%d,%d] gyro=[%d,%d,%d]%s\n",
               ok && !stuck && !railed ? "FUNCTIONAL" : "FAULT",
               a[0], a[1], a[2], g[0], g[1], g[2],
               stuck  ? " (stuck zero)" :
               railed ? " (bus railed)" : "");
    } else {
        printf("[SENS] MPU6050:  NOT DETECTED on I2C\n");
    }

    // MAX30100 — rulează deja continuu de la setup; aşteaptă FIFO să se umple
    if (max_present) {
        vTaskDelay(pdMS_TO_TICKS(100));   // ~10 sample-uri la 100Hz
        uint16_t ir = 0, red = 0;
        bool ok = max_read(&ir, &red);
        printf("[SENS] MAX30100: %s | IR=%u RED=%u%s\n",
               ok ? "FUNCTIONAL" : "FAULT", ir, red,
               (ok && ir == 0 && red == 0) ? " (no finger / FIFO empty)" : "");
    } else {
        printf("[SENS] MAX30100: NOT DETECTED on I2C\n");
    }

    // OLED
    printf("[SENS] SSD1306:  %s (addr 0x%02X ch%d)\n",
           oled_present ? "FUNCTIONAL" : "FAULT", OLED_ADDR, MUX_CH_OLED);

    // DS18B20 — full convert cycle: start, wait 750ms, read
    printf("[SENS] DS18B20:  starting conversion...\n");
    bool ds_started = ds_start_conv();
    if (ds_started) {
        vTaskDelay(pdMS_TO_TICKS(DS18B20_CONV_MS + 50));
        float t = 0.0f;
        bool ok = ds_read_temp(&t);
        printf("[SENS] DS18B20:  %s | temp=%.2f C\n",
               ok ? "FUNCTIONAL" : "FAULT (no response / CRC)", t);
    } else {
        printf("[SENS] DS18B20:  FAULT (no presence pulse on GPIO%d)\n", DS18B20_PIN);
    }

    // GPS — citeşte 200 ms din UART, verifică dacă vin caractere NMEA
    uint8_t buf[128];
    int n = uart_read_bytes(GPS_UART_NUM, buf, sizeof(buf) - 1, pdMS_TO_TICKS(200));
    bool gps_chars = false, gps_dollar = false;
    for (int i = 0; i < n; i++) {
        if (buf[i] >= 0x20 && buf[i] < 0x7F) gps_chars = true;
        if (buf[i] == '$') gps_dollar = true;
    }
    if (n > 0) gps_feed(buf, n);
    printf("[SENS] NEO-6M:   %s | %d bytes in 200ms%s\n",
           gps_dollar ? "FUNCTIONAL" : (gps_chars ? "PARTIAL" : "NO DATA"),
           n, gps_dollar ? " (NMEA detected)" : "");

    printf("[SENS] ===== END SENSOR TEST =====\n\n");
}

// =============================================================================
// JSON builder — fields match ESPDataResponseDTO property names (camelCase)
// =============================================================================
static void build_json(char *buf, size_t sz,
                       bool mpu_ok, const int16_t *accel, const int16_t *gyro,
                       bool max_ok, uint16_t ir, uint16_t red,
                       bool ds_ok,  float temp,
                       uint64_t ts)
{
    // Consum atomic al flag-ului de cădere (set de fall_task, raportat o singură dată)
    bool is_fall = g_fall_detected;
    if (is_fall) g_fall_detected = false;

    // Consum atomic al acumulatorului de mişcare: snapshot + reset, ca să următoarea fereastră
    // să fie independentă (~5s, deci ~250 sample-uri la 50 Hz prin fall_task).
    movement_accumulator_t snap = {0, 0.0f, 0.0f};
    if (s_movement_lock && xSemaphoreTake(s_movement_lock, pdMS_TO_TICKS(50)) == pdTRUE) {
        snap = g_movement;
        g_movement.sample_count = 0;
        g_movement.dev_sum      = 0.0f;
        g_movement.dev_max      = 0.0f;
        xSemaphoreGive(s_movement_lock);
    }

    // Default conservativ: dacă nu avem destule sample-uri (MPU oprit, sleep) NU declarăm
    // ceva — server-ul tratează absenţa câmpului ca lipsă de date.
    const char *activity = NULL;
    if (snap.sample_count >= 10) {
        float mean_dev = snap.dev_sum / (float)snap.sample_count;
        // Combinăm media + peak: mers susţinut creşte media; un peak izolat (ridicat în
        // picioare, întors în pat) NU ridică media dar e captat de dev_max.
        bool is_moving = (mean_dev > MOVEMENT_MEAN_THRESHOLD_G) ||
                         (snap.dev_max > MOVEMENT_PEAK_THRESHOLD_G);
        activity = is_moving ? "moving" : "stationary";
    }

    int n = snprintf(buf, sz,
        "{\"serial\":\"%s\",\"date\":%llu,\"isAvailable\":true,\"isFall\":%s,"
        "\"mpu6050\":[%d,%d,%d],\"gyro\":[%d,%d,%d],",
        g_serial, (unsigned long long)ts, is_fall ? "true" : "false",
        mpu_ok ? accel[0] : 0, mpu_ok ? accel[1] : 0, mpu_ok ? accel[2] : 0,
        mpu_ok ? gyro[0]  : 0, mpu_ok ? gyro[1]  : 0, mpu_ok ? gyro[2]  : 0);

    if (activity)
        n += snprintf(buf+n, sz-n, "\"activity\":\"%s\",", activity);

    if (max_ok) n += snprintf(buf+n, sz-n, "\"max30100\":[%u,%u],\"bpm\":%d,\"spo2\":%d,", ir, red, g_bpm, g_spo2);
    else        n += snprintf(buf+n, sz-n, "\"max30100\":null,\"bpm\":0,");

    if (ds_ok) n += snprintf(buf+n, sz-n, "\"temperature\":%.2f,", temp);
    else       n += snprintf(buf+n, sz-n, "\"temperature\":null,");

    if (g_gps.valid)
        snprintf(buf+n, sz-n, "\"neo6m\":\"%.6f,%.6f\"}", g_gps.lat, g_gps.lon);
    else
        snprintf(buf+n, sz-n, "\"neo6m\":null}");
}

// =============================================================================
// app_main
// =============================================================================
extern "C" void app_main(void)
{
    setvbuf(stdout, NULL, _IONBF, 0);

    // Mutex I2C recursiv — creat înainte de orice acces I2C concurent
    s_i2c_lock = xSemaphoreCreateRecursiveMutex();

    // Mutex pentru acumulatorul de mişcare (fall_task scrie 50 Hz, build_json citeşte la POST)
    s_movement_lock = xSemaphoreCreateMutex();

    printf("\n=== LifeAlertPlus | ESP32-C3 Mini ===\n\n");

    // Sanity check on Kconfig-provided secrets — make missing values loud rather than silent.
    if (API_DEVICE_KEY[0] == '\0')
        printf("[CFG] WARNING: CONFIG_LIFEALERT_API_DEVICE_KEY is empty. API calls will be rejected (401).\n");
    if (API_BASE_URL[0] == '\0')
        printf("[CFG] WARNING: CONFIG_LIFEALERT_API_BASE_URL is empty. No HTTP requests will be issued.\n");

    // Unique serial derived from factory MAC (set monitored.DeviceSerialNumber to this value)
    uint8_t mac[6];
    esp_read_mac(mac, ESP_MAC_WIFI_STA);
    snprintf(g_serial, sizeof(g_serial), "ESP32-%02X%02X%02X%02X%02X%02X",
             mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);
    printf("[ID] %s\n", g_serial);

    // I2C + multiplexer
    i2c_setup();
    vTaskDelay(pdMS_TO_TICKS(100));
    bool mux_ok = mux_setup();
    printf("[MUX     ] %s | 0x%02X\n", mux_ok ? "OK " : "---", g_mux_addr);

    // Sensors
    bool mpu_ok = mpu_setup();
    g_mpu_ok = mpu_ok;
    printf("[MPU6050 ] %s | ch%d 0x%02X\n", mpu_ok ? "OK " : "---", MUX_CH_MPU6050, g_mpu_addr);

    bool max_ok = max_setup();
    g_max_ok = max_ok;
    // LED-ul rămâne aprins continuu (sleep doar prin butonul 2 / sleep mode)
    printf("[MAX30100] %s | ch%d 0x%02X (LED ON)\n", max_ok ? "OK " : "---", MUX_CH_MAX30100, M30_ADDR);

    g_oled_ok = oled_setup();
    if (g_oled_ok) { oled_clear(); oled_show_title(); }
    printf("[OLED    ] %s | ch%d 0x%02X\n", g_oled_ok ? "OK " : "---", MUX_CH_OLED, OLED_ADDR);

    ds_gpio_init();
    bool ds_conv    = ds_start_conv();
    g_ds_ok = ds_conv;
    uint32_t ds_t0  = xTaskGetTickCount() * portTICK_PERIOD_MS;
    float    ds_temp = 0.0f;
    bool     ds_ready = false;
    printf("[DS18B20 ] %s | GPIO %d\n", ds_conv ? "OK " : "---", DS18B20_PIN);

    gps_setup();
    printf("[GPS     ] OK | TX=%d RX=%d\n", GPS_TX_PIN, GPS_RX_PIN);

    // LEDs
    gpio_config_t led_conf = {};
    led_conf.pin_bit_mask = (1ULL << LED_GREEN_PIN) | (1ULL << LED_YELLOW_PIN);
    led_conf.mode         = GPIO_MODE_OUTPUT;
    led_conf.intr_type    = GPIO_INTR_DISABLE;
    gpio_config(&led_conf);
    gpio_set_level((gpio_num_t)LED_GREEN_PIN,  1);  // green on — active-HIGH (ca galbenul)
    gpio_set_level((gpio_num_t)LED_YELLOW_PIN, 0);  // yellow off initially

    // Buttons
    gpio_install_isr_service(0);
    buttons_setup();

#if HW_SELFTEST
    hw_selftest();
#endif

    // Verifică prezenţa ŞI funcţionalitatea senzorilor (citire reală de valori)
    sensors_self_test(mpu_ok, max_ok, g_oled_ok, mux_ok);

    // Reia conversia DS18B20 după self-test (acesta a făcut propriul ciclu)
    ds_conv = ds_start_conv();
    ds_t0   = xTaskGetTickCount() * portTICK_PERIOD_MS;

    // WiFi — LED2 lights up on connect
    bool wifi_ok = wifi_setup();
    if (wifi_ok) {
        gpio_set_level((gpio_num_t)LED_YELLOW_PIN, 0);  // yellow off = connected OK
        printf("[WiFi] Connected\n");
    } else {
        gpio_set_level((gpio_num_t)LED_YELLOW_PIN, 1);  // yellow on = offline
        printf("[WiFi] FAILED — running offline\n");
    }

    // Offline queue (NVS must be initialised inside wifi_setup already)
    queue_init();
    panic_queue_init();

    // Heartbeat timer — fires every HEARTBEAT_MS
    esp_timer_create_args_t hb_args = {};
    hb_args.callback = heartbeat_send;
    hb_args.name     = "heartbeat";
    esp_timer_create(&hb_args, &s_hb_timer);
    esp_timer_start_periodic(s_hb_timer, (uint64_t)HEARTBEAT_MS * 1000ULL);

    // Sleep-off timer (one-shot) — stinge LED galben + OLED după 5s în sleep
    esp_timer_create_args_t so_args = {};
    so_args.callback = sleep_off_cb;
    so_args.name     = "sleep_off";
    esp_timer_create(&so_args, &s_sleep_off_timer);

    // Pulse task — eşantionează MAX30100 la 100Hz şi calculează BPM
    if (g_max_ok) xTaskCreate(pulse_task, "pulse", 4096, NULL, 5, NULL);

    // Fall detection task — eşantionează MPU6050 la 50Hz şi rulează state machine free-fall/impact/stillness
    if (g_mpu_ok) xTaskCreate(fall_task, "fall", 4096, NULL, 5, NULL);

    printf("\n[SYS] Monitoring active | POST every %d ms\n", API_POST_MS);
    printf("=====================================\n\n");

    int16_t accel[3] = {}, gyro[3] = {};
    uint16_t ir = 0, red = 0;
    uint32_t last_post_ms = 0;
    static char json_buf[512];

    while (true) {
        uint32_t now = xTaskGetTickCount() * portTICK_PERIOD_MS;

        // MPU6050: citit la fiecare iteraţie pentru detecţia căzăturilor
        bool mpu_ok_r = mpu_ok && mpu_read(accel, gyro);

        // DS18B20 non-blocking: citeşte rezultatul după fereastra de 750 ms
        if (ds_conv && (now - ds_t0) >= DS18B20_CONV_MS) {
            ds_ready = ds_read_temp(&ds_temp);
            ds_conv  = false;
        }
        if (!ds_conv) {
            ds_conv = ds_start_conv();
            ds_t0   = now;
        }

        // GPS — UART driver bufferează datele, citim pasiv
        int gps_n = uart_read_bytes(GPS_UART_NUM, g_gps_rx, sizeof(g_gps_rx) - 1, pdMS_TO_TICKS(20));
        if (gps_n > 0) gps_feed(g_gps_rx, gps_n);

        // La intervalul de POST: trezeşte MAX30100, citeşte, pune-l la somn,
        // actualizează OLED şi trimite datele (skipped complet în sleep mode)
        if ((now - last_post_ms) >= (uint32_t)API_POST_MS) {
            last_post_ms = now;  // actualizează mereu — evită POST imediat la wake
            if (!g_sleep_mode) {
                // MAX30100 e eşantionat continuu de pulse_task — luăm valorile actuale
                ir  = g_last_ir;
                red = g_last_red;
                bool max_ok_r = g_max_ok && g_finger_on;

                oled_show_data(ds_ready, ds_temp);

                build_json(json_buf, sizeof(json_buf),
                           mpu_ok_r, accel, gyro,
                           max_ok_r, ir, red,
                           ds_ready, ds_temp,
                           (uint64_t)now);
                printf("[JSON] %s\n", json_buf);
                if (s_wifi_ok) {
                    panic_queue_flush();   // panic-ul are prioritate
                    queue_flush();
                    if (http_post(json_buf)) {
                        printf("[POST] OK -> server\n");
                    } else {
                        printf("[POST] FAILED -> enqueued offline\n");
                        queue_push(json_buf);
                    }
                } else {
                    printf("[POST] OFFLINE -> enqueued (queue=%lu)\n",
                           (unsigned long)(s_q_count + 1));
                    queue_push(json_buf);
                }
            }
        }

        vTaskDelay(pdMS_TO_TICKS(SENSOR_READ_MS));
    }
}
