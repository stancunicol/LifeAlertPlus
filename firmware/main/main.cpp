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
 *   GPIO 7  — I2C SDA (TCA9548)
 *   GPIO 8  — I2C SCL (TCA9548)
 */

#include <stdio.h>
#include <math.h>
#include <string.h>
#include <stdlib.h>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "freertos/event_groups.h"
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

// ─── WiFi / API ───────────────────────────────────────────────────────────────
// Fallback credentials used only when NVS has no stored networks
#define WIFI_SSID_DEFAULT  "Nicole"
#define WIFI_PASS_DEFAULT  "20042005"
#define WIFI_MAX_RETRY     5

// Must match Urls:EspDeviceKey in API appsettings
#define API_BASE_URL       "https://api-lifealertplusiot-gqf3crdrenfgd9bw.germanywestcentral-01.azurewebsites.net/"
#define API_INGEST_PATH    "/api/ESP/ingest"
#define API_PANIC_PATH     "/api/ESP/panic"
#define API_HEARTBEAT_PATH "/api/ESP/heartbeat"
#define API_DEVICE_KEY     "idontknowwhattoputhere"
#define FIRMWARE_VERSION   "1.1.0"

// ─── Timing ───────────────────────────────────────────────────────────────────
#define SENSOR_READ_MS     500      // inner loop period
#define API_POST_MS        10000    // how often to POST measurements
#define HEARTBEAT_MS       300000   // heartbeat every 5 minutes
#define SLEEP_POST_MS      10000    // POST interval in power-save mode

// ─── Offline queue (NVS) ──────────────────────────────────────────────────────
#define QUEUE_NS           "offl_q"
#define QUEUE_MAX          50

// ─── I2C ──────────────────────────────────────────────────────────────────────
#define I2C_SDA_PIN      7
#define I2C_SCL_PIN      8
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
static QueueHandle_t s_btn_q = NULL;

// Power-save mode (Button 2 toggle)
static volatile bool g_sleep_mode = false;

// Heartbeat
static esp_timer_handle_t s_hb_timer = NULL;

// OLED availability (set once in app_main)
static bool g_oled_ok = false;

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
    mux_open(MUX_CH_MPU6050);
    uint8_t reg = MPU_ACCEL_XOUT_H;
    uint8_t d[14];
    esp_err_t e = i2c_master_write_read_device(
        I2C_PORT, g_mpu_addr, &reg, 1, d, 14, pdMS_TO_TICKS(I2C_TIMEOUT_MS));
    mux_close();
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
    d[0] = M30_LED_CFG;  d[1] = 0x24;   // medium LED current
    i2c_master_write_to_device(I2C_PORT, M30_ADDR, d, 2, pdMS_TO_TICKS(I2C_TIMEOUT_MS));
    d[0] = M30_FIFO_WR;  d[1] = 0x00;   // clear FIFO
    i2c_master_write_to_device(I2C_PORT, M30_ADDR, d, 2, pdMS_TO_TICKS(I2C_TIMEOUT_MS));
    mux_close();
    return true;
}

static bool max_read(uint16_t *ir, uint16_t *red)
{
    mux_open(MUX_CH_MAX30100);
    uint8_t reg = M30_FIFO_DATA;
    uint8_t d[4];
    esp_err_t e = i2c_master_write_read_device(
        I2C_PORT, M30_ADDR, &reg, 1, d, 4, pdMS_TO_TICKS(I2C_TIMEOUT_MS));
    mux_close();
    if (e != ESP_OK) return false;
    *ir  = (uint16_t)((d[0] << 8) | d[1]);
    *red = (uint16_t)((d[2] << 8) | d[3]);
    return true;
}

static void max_sleep(void)
{
    mux_open(MUX_CH_MAX30100);
    uint8_t d[2] = { M30_MODE_CFG, 0x00 };   // power-down
    i2c_master_write_to_device(I2C_PORT, M30_ADDR, d, 2, pdMS_TO_TICKS(I2C_TIMEOUT_MS));
    mux_close();
}

static void max_wake(void)
{
    mux_open(MUX_CH_MAX30100);
    uint8_t d[2] = { M30_MODE_CFG, 0x03 };   // SpO2 mode
    i2c_master_write_to_device(I2C_PORT, M30_ADDR, d, 2, pdMS_TO_TICKS(I2C_TIMEOUT_MS));
    mux_close();
    vTaskDelay(pdMS_TO_TICKS(20));            // stabilizare ADC
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

// Scrie un string pe OLED la pagina şi coloana date (6px per caracter)
static void oled_write_str(uint8_t page, uint8_t col, const char *s)
{
    mux_open(MUX_CH_OLED);
    oled_cmd((uint8_t)(0xB0 + page));
    oled_cmd((uint8_t)(col & 0x0F));
    oled_cmd((uint8_t)(0x10 | (col >> 4)));
    while (*s && col < 128) {
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
    mux_close();
}

// Afişează ultimele date citite pe OLED (apelat la fiecare POST)
static void oled_show_data(
    bool ds_ok,  float ds_temp,
    bool max_ok, uint16_t ir, uint16_t red,
    bool mpu_ok, const int16_t *accel)
{
    if (!g_oled_ok) return;

    char line[22];

    // Page 0: titlu centrat (10 chars × 6px = 60px → start col 34)
    oled_write_str(0, 34, "LifeAlert+");

    // Page 2: temperatură (evitam %f — folosim aritmetică pe int)
    if (ds_ok) {
        bool neg = (ds_temp < 0.0f);
        int ti = (int)(fabsf(ds_temp) * 10.0f + 0.5f);
        snprintf(line, sizeof(line), "Temp:%s%d.%dC        ",
                 neg ? "-" : " ", ti / 10, ti % 10);
    } else {
        snprintf(line, sizeof(line), "Temp: --.-C        ");
    }
    oled_write_str(2, 0, line);

    // Page 3: MAX30100
    if (max_ok) {
        snprintf(line, sizeof(line), "IR:%-5u  R:%-5u  ", ir, red);
    } else {
        snprintf(line, sizeof(line), "IR:-----  R:-----  ");
    }
    oled_write_str(3, 0, line);

    // Page 4: accelerometru X/Y
    if (mpu_ok) {
        snprintf(line, sizeof(line), "AX:%-6d AY:%-5d", (int)accel[0], (int)accel[1]);
    } else {
        snprintf(line, sizeof(line), "AX:------  AY:-----");
    }
    oled_write_str(4, 0, line);

    // Page 5: accelerometru Z
    if (mpu_ok) {
        snprintf(line, sizeof(line), "AZ:%-6d           ", (int)accel[2]);
    } else {
        snprintf(line, sizeof(line), "AZ:------           ");
    }
    oled_write_str(5, 0, line);

    // Page 6: WiFi + coadă offline
    snprintf(line, sizeof(line), "WiFi:%-3s  Q:%-6lu  ",
             s_wifi_ok ? "OK" : "---",
             (unsigned long)s_q_count);
    oled_write_str(6, 0, line);
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
    if (s_wifi_net_count == 0) {
        strncpy(s_wifi_ssids[0],  WIFI_SSID_DEFAULT, 32);
        strncpy(s_wifi_passes[0], WIFI_PASS_DEFAULT, 64);
        s_wifi_net_count = 1;
    }
    printf("[WiFi] %u network(s) configured\n", s_wifi_net_count);
}

static bool wifi_try_connect(uint8_t idx)
{
    s_wifi_retry = 0;
    xEventGroupClearBits(s_wifi_eg, WIFI_OK_BIT | WIFI_FAIL_BIT);

    wifi_config_t wc = {};
    strncpy((char *)wc.sta.ssid,     s_wifi_ssids[idx],  sizeof(wc.sta.ssid)  - 1);
    strncpy((char *)wc.sta.password, s_wifi_passes[idx], sizeof(wc.sta.password) - 1);
    wc.sta.threshold.authmode = WIFI_AUTH_WPA2_PSK;

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

    ESP_ERROR_CHECK(esp_netif_init());
    ESP_ERROR_CHECK(esp_event_loop_create_default());
    esp_netif_create_default_wifi_sta();

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
    cfg.timeout_ms        = 5000;
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

static void http_post(const char *json) { http_post_to(API_INGEST_PATH, json); }

// =============================================================================
// Offline queue — NVS ring buffer, max QUEUE_MAX measurements
// =============================================================================
static nvs_handle_t s_q_nvs = 0;
static uint32_t     s_q_head = 0, s_q_tail = 0, s_q_count = 0;

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
    printf("[Q] Flushing %lu measurements...\n", (unsigned long)s_q_count);
    while (s_q_count > 0 && s_wifi_ok) {
        char key[8];
        snprintf(key, sizeof(key), "%lu", (unsigned long)s_q_head);
        char json[512] = {};
        size_t len = sizeof(json);
        if (nvs_get_str(s_q_nvs, key, json, &len) != ESP_OK) {
            // corrupt entry — skip
            s_q_head = (s_q_head + 1) % QUEUE_MAX;
            s_q_count--;
            continue;
        }
        if (!http_post_to(API_INGEST_PATH, json)) break; // stop on failure
        nvs_erase_key(s_q_nvs, key);
        s_q_head = (s_q_head + 1) % QUEUE_MAX;
        s_q_count--;
        nvs_set_u32(s_q_nvs, "head",  s_q_head);
        nvs_set_u32(s_q_nvs, "count", s_q_count);
        nvs_commit(s_q_nvs);
    }
    if (s_q_count == 0) printf("[Q] Flush complete\n");
}

// =============================================================================
// Heartbeat
// =============================================================================

static void heartbeat_send(void *)
{
    if (!s_wifi_ok) return;
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
    if (http_post_to(API_PANIC_PATH, json))
        printf("[BTN] Panic sent\n");
    else
        printf("[BTN] Panic send FAILED\n");
}

// =============================================================================
// Buttons
// =============================================================================
static void IRAM_ATTR btn_isr(void *arg)
{
    int pin   = (int)(intptr_t)arg;
    int event = (pin << 8) | gpio_get_level((gpio_num_t)pin);
    xQueueSendFromISR(s_btn_q, &event, NULL);
}

static void btn_task(void *)
{
    int event;
    while (true) {
        if (xQueueReceive(s_btn_q, &event, portMAX_DELAY)) {
            int pin   = (event >> 8) & 0xFF;
            int level = event & 1;
            if (level == 0) {  // pressed (active-low)
                if (pin == BUTTON1_PIN) {
                    printf("[BTN] Panic!\n");
                    // Flash yellow LED twice as visual feedback
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
                        esp_wifi_set_ps(WIFI_PS_MAX_MODEM);
                        gpio_set_level((gpio_num_t)LED_YELLOW_PIN, 1);
                        printf("[BTN] Sleep mode ON  (POST every %d ms)\n", SLEEP_POST_MS);
                    } else {
                        esp_wifi_set_ps(WIFI_PS_NONE);
                        gpio_set_level((gpio_num_t)LED_YELLOW_PIN, 0);
                        printf("[BTN] Sleep mode OFF (POST every %d ms)\n", API_POST_MS);
                    }
                }
            }
        }
    }
}

static void buttons_setup(void)
{
    s_btn_q = xQueueCreate(10, sizeof(int));
    xTaskCreate(btn_task, "btn", 4096, NULL, 5, NULL);
    gpio_config_t c = {};
    c.pin_bit_mask = (1ULL << BUTTON1_PIN) | (1ULL << BUTTON2_PIN);
    c.mode         = GPIO_MODE_INPUT;
    c.pull_up_en   = GPIO_PULLUP_ENABLE;
    c.intr_type    = GPIO_INTR_ANYEDGE;
    gpio_config(&c);
    gpio_isr_handler_add((gpio_num_t)BUTTON1_PIN, btn_isr, (void *)(intptr_t)BUTTON1_PIN);
    gpio_isr_handler_add((gpio_num_t)BUTTON2_PIN, btn_isr, (void *)(intptr_t)BUTTON2_PIN);
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
    int n = snprintf(buf, sz,
        "{\"serial\":\"%s\",\"date\":%llu,\"isAvailable\":true,"
        "\"mpu6050\":[%d,%d,%d],\"gyro\":[%d,%d,%d],",
        g_serial, (unsigned long long)ts,
        mpu_ok ? accel[0] : 0, mpu_ok ? accel[1] : 0, mpu_ok ? accel[2] : 0,
        mpu_ok ? gyro[0]  : 0, mpu_ok ? gyro[1]  : 0, mpu_ok ? gyro[2]  : 0);

    if (max_ok) n += snprintf(buf+n, sz-n, "\"max30100\":[%u,%u],", ir, red);
    else        n += snprintf(buf+n, sz-n, "\"max30100\":null,");

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
    printf("\n=== LifeAlertPlus | ESP32-C3 Mini ===\n\n");

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
    printf("[MPU6050 ] %s | ch%d 0x%02X\n", mpu_ok ? "OK " : "---", MUX_CH_MPU6050, g_mpu_addr);

    bool max_ok = max_setup();
    if (max_ok) max_sleep();   // doarme până la primul POST
    printf("[MAX30100] %s | ch%d 0x%02X\n", max_ok ? "OK " : "---", MUX_CH_MAX30100, M30_ADDR);

    g_oled_ok = oled_setup();
    if (g_oled_ok) { oled_clear(); oled_show_title(); }
    printf("[OLED    ] %s | ch%d 0x%02X\n", g_oled_ok ? "OK " : "---", MUX_CH_OLED, OLED_ADDR);

    ds_gpio_init();
    bool ds_conv    = ds_start_conv();
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
    gpio_set_level((gpio_num_t)LED_GREEN_PIN,  1);  // green on — system running
    gpio_set_level((gpio_num_t)LED_YELLOW_PIN, 0);  // yellow off initially

    // Buttons
    gpio_install_isr_service(0);
    buttons_setup();

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

    // Heartbeat timer — fires every HEARTBEAT_MS
    esp_timer_create_args_t hb_args = {};
    hb_args.callback = heartbeat_send;
    hb_args.name     = "heartbeat";
    esp_timer_create(&hb_args, &s_hb_timer);
    esp_timer_start_periodic(s_hb_timer, (uint64_t)HEARTBEAT_MS * 1000ULL);

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
        // actualizează OLED şi trimite datele
        uint32_t post_interval = g_sleep_mode ? SLEEP_POST_MS : API_POST_MS;
        if ((now - last_post_ms) >= post_interval) {
            if (max_ok) max_wake();
            bool max_ok_r = max_ok && max_read(&ir, &red);
            if (max_ok) max_sleep();

            oled_show_data(ds_ready, ds_temp, max_ok_r, ir, red, mpu_ok_r, accel);

            build_json(json_buf, sizeof(json_buf),
                       mpu_ok_r, accel, gyro,
                       max_ok_r, ir, red,
                       ds_ready, ds_temp,
                       (uint64_t)now);
            if (s_wifi_ok) {
                queue_flush();
                http_post(json_buf);
            } else {
                queue_push(json_buf);
            }
            last_post_ms = now;
        }

        vTaskDelay(pdMS_TO_TICKS(SENSOR_READ_MS));
    }
}
