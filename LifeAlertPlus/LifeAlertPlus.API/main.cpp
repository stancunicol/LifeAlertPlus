/*
 * LifeAlertPlus — ESP32-C3 Mini Firmware
 *
 * Sensors  : MPU6050 (accel/gyro), MAX30100 (SpO2/HR), DS18B20 (temp), NEO-6M GPS
 * Display  : OLED SSD1306 via PCA9548 multiplexer (ch 1)
 * Network  : WiFi → HTTPS POST to LifeAlertPlus API every API_POST_MS ms
 *
 * GPIO map (ESP32-C3 Mini):
 *   GPIO 0  — DS18B20 (1-Wire)
 *   GPIO 1  — Buzzer
 *   GPIO 2  — GPS RX  (UART1)
 *   GPIO 3  — GPS TX  (UART1)
 *   GPIO 4  — Button 1 (alarm)
 *   GPIO 5  — Button 2 (reset)
 *   GPIO 6  — LED 2
 *   GPIO 7  — LED 1
 *   GPIO 8  — I2C SDA
 *   GPIO 9  — I2C SCL
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
#include "esp_wifi.h"
#include "esp_event.h"
#include "esp_log.h"
#include "esp_crt_bundle.h"
#include "esp_http_client.h"
#include "nvs_flash.h"
#include "lwip/inet.h"

// ─── WiFi / API ───────────────────────────────────────────────────────────────
#define WIFI_SSID        "YOUR_SSID"
#define WIFI_PASS        "YOUR_PASSWORD"
#define WIFI_MAX_RETRY   5

// Must match Urls:EspDeviceKey in API appsettings
#define API_BASE_URL     "https://your-api.azurewebsites.net"
#define API_INGEST_PATH  "/api/ESP/ingest"
#define API_DEVICE_KEY   "change-me-to-a-strong-random-key"

// ─── Timing ───────────────────────────────────────────────────────────────────
#define SENSOR_READ_MS   500     // inner loop period
#define API_POST_MS      2000    // how often to POST (must be >= SENSOR_READ_MS)

// ─── I2C ──────────────────────────────────────────────────────────────────────
#define I2C_SDA_PIN      8
#define I2C_SCL_PIN      9
#define I2C_PORT         I2C_NUM_0
#define I2C_FREQ_HZ      100000
#define I2C_TIMEOUT_MS   50

// ─── PCA9548 multiplexer ──────────────────────────────────────────────────────
#define MUX_ADDR_MIN     0x70
#define MUX_ADDR_MAX     0x77
#define MUX_CH_MPU6050   2
#define MUX_CH_MAX30100  0
#define MUX_CH_OLED      1

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
#define GPS_TX_PIN       3
#define GPS_RX_PIN       2
#define GPS_RX_BUF       1024

// ─── GPIO ─────────────────────────────────────────────────────────────────────
#define BUZZER_PIN       1
#define BUTTON1_PIN      4
#define BUTTON2_PIN      5
#define LED1_PIN         7
#define LED2_PIN         6

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
        gpio_set_level((gpio_num_t)LED2_PIN, 0);
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

static bool wifi_setup(void)
{
    s_wifi_eg = xEventGroupCreate();

    esp_err_t ret = nvs_flash_init();
    if (ret == ESP_ERR_NVS_NO_FREE_PAGES || ret == ESP_ERR_NVS_NEW_VERSION_FOUND) {
        nvs_flash_erase();
        ret = nvs_flash_init();
    }
    ESP_ERROR_CHECK(ret);

    ESP_ERROR_CHECK(esp_netif_init());
    ESP_ERROR_CHECK(esp_event_loop_create_default());
    esp_netif_create_default_wifi_sta();

    wifi_init_config_t cfg = WIFI_INIT_CONFIG_DEFAULT();
    ESP_ERROR_CHECK(esp_wifi_init(&cfg));

    esp_event_handler_instance_register(WIFI_EVENT, ESP_EVENT_ANY_ID,    on_wifi_event, NULL, NULL);
    esp_event_handler_instance_register(IP_EVENT,   IP_EVENT_STA_GOT_IP, on_wifi_event, NULL, NULL);

    wifi_config_t wc = {};
    strncpy((char *)wc.sta.ssid,     WIFI_SSID, sizeof(wc.sta.ssid)     - 1);
    strncpy((char *)wc.sta.password, WIFI_PASS, sizeof(wc.sta.password) - 1);
    wc.sta.threshold.authmode = WIFI_AUTH_WPA2_PSK;

    ESP_ERROR_CHECK(esp_wifi_set_mode(WIFI_MODE_STA));
    ESP_ERROR_CHECK(esp_wifi_set_config(WIFI_IF_STA, &wc));
    ESP_ERROR_CHECK(esp_wifi_start());

    EventBits_t bits = xEventGroupWaitBits(
        s_wifi_eg, WIFI_OK_BIT | WIFI_FAIL_BIT, pdFALSE, pdFALSE, pdMS_TO_TICKS(15000));
    return (bits & WIFI_OK_BIT) != 0;
}

// =============================================================================
// HTTP POST — sends JSON matching ESPDataResponseDTO
// =============================================================================
static void http_post(const char *json)
{
    if (!s_wifi_ok) return;

    esp_http_client_config_t cfg = {};
    cfg.url               = API_BASE_URL API_INGEST_PATH;
    cfg.method            = HTTP_METHOD_POST;
    cfg.timeout_ms        = 5000;
    cfg.crt_bundle_attach = esp_crt_bundle_attach;  // verifies Azure TLS cert

    esp_http_client_handle_t client = esp_http_client_init(&cfg);
    if (!client) return;

    esp_http_client_set_header(client, "Content-Type", "application/json");
    esp_http_client_set_header(client, "X-Device-Key", API_DEVICE_KEY);
    esp_http_client_set_post_field(client, json, (int)strlen(json));

    esp_err_t err = esp_http_client_perform(client);
    if (err == ESP_OK) {
        int code = esp_http_client_get_status_code(client);
        if (code != 200) printf("[HTTP] Status %d\n", code);
    } else {
        printf("[HTTP] %s\n", esp_err_to_name(err));
    }
    esp_http_client_cleanup(client);
}

// =============================================================================
// Buzzer
// =============================================================================
static void buzzer_setup(void)
{
    gpio_config_t c = {};
    c.pin_bit_mask = (1ULL << BUZZER_PIN);
    c.mode         = GPIO_MODE_OUTPUT;
    c.intr_type    = GPIO_INTR_DISABLE;
    gpio_config(&c);
    gpio_set_level((gpio_num_t)BUZZER_PIN, 0);
}

static void buzzer_beep(int ms)
{
    gpio_set_level((gpio_num_t)BUZZER_PIN, 1);
    vTaskDelay(pdMS_TO_TICKS(ms));
    gpio_set_level((gpio_num_t)BUZZER_PIN, 0);
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
            if (level == 0) {  // pressed
                if (pin == BUTTON1_PIN) { printf("[BTN] Alarm\n"); buzzer_beep(1000); }
                else                    { printf("[BTN] Reset\n"); }
            }
        }
    }
}

static void buttons_setup(void)
{
    s_btn_q = xQueueCreate(10, sizeof(int));
    xTaskCreate(btn_task, "btn", 2048, NULL, 5, NULL);
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
    esp_efuse_mac_get_default(mac);
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
    printf("[MAX30100] %s | ch%d 0x%02X\n", max_ok ? "OK " : "---", MUX_CH_MAX30100, M30_ADDR);

    bool oled_ok = oled_setup();
    if (oled_ok) { oled_clear(); oled_show_title(); }
    printf("[OLED    ] %s | ch%d 0x%02X\n", oled_ok ? "OK " : "---", MUX_CH_OLED, OLED_ADDR);

    ds_gpio_init();
    bool ds_conv    = ds_start_conv();
    uint32_t ds_t0  = xTaskGetTickCount() * portTICK_PERIOD_MS;
    float    ds_temp = 0.0f;
    bool     ds_ready = false;
    printf("[DS18B20 ] %s | GPIO %d\n", ds_conv ? "OK " : "---", DS18B20_PIN);

    gps_setup();
    printf("[GPS     ] OK | TX=%d RX=%d\n", GPS_TX_PIN, GPS_RX_PIN);

    // Buzzer + LEDs
    buzzer_setup();
    gpio_config_t led_conf = {};
    led_conf.pin_bit_mask = (1ULL << LED1_PIN) | (1ULL << LED2_PIN);
    led_conf.mode         = GPIO_MODE_OUTPUT;
    led_conf.intr_type    = GPIO_INTR_DISABLE;
    gpio_config(&led_conf);
    gpio_set_level((gpio_num_t)LED1_PIN, 1);
    gpio_set_level((gpio_num_t)LED2_PIN, 0);

    // Buttons
    gpio_install_isr_service(0);
    buttons_setup();

    // WiFi — LED2 lights up on connect
    printf("[WiFi] Connecting to \"%s\"...\n", WIFI_SSID);
    bool wifi_ok = wifi_setup();
    if (wifi_ok) {
        gpio_set_level((gpio_num_t)LED2_PIN, 1);
        buzzer_beep(80);
        printf("[WiFi] Connected\n");
    } else {
        printf("[WiFi] FAILED — running offline\n");
    }

    printf("\n[SYS] Monitoring active | POST every %d ms\n", API_POST_MS);
    printf("=====================================\n\n");

    int16_t accel[3] = {}, gyro[3] = {};
    uint16_t ir = 0, red = 0;
    uint32_t last_post_ms = 0;
    static char json_buf[512];

    while (true) {
        uint32_t now = xTaskGetTickCount() * portTICK_PERIOD_MS;

        bool mpu_ok_r = mpu_ok && mpu_read(accel, gyro);
        bool max_ok_r = max_ok && max_read(&ir, &red);

        // DS18B20 non-blocking: read result after 750 ms conversion window
        if (ds_conv && (now - ds_t0) >= DS18B20_CONV_MS) {
            ds_ready = ds_read_temp(&ds_temp);
            ds_conv  = false;
        }
        if (!ds_conv) {
            ds_conv = ds_start_conv();
            ds_t0   = now;
        }

        // GPS
        int gps_n = uart_read_bytes(GPS_UART_NUM, g_gps_rx, sizeof(g_gps_rx) - 1, pdMS_TO_TICKS(20));
        if (gps_n > 0) gps_feed(g_gps_rx, gps_n);

        // POST to API at configured interval
        if ((now - last_post_ms) >= API_POST_MS) {
            build_json(json_buf, sizeof(json_buf),
                       mpu_ok_r, accel, gyro,
                       max_ok_r, ir, red,
                       ds_ready, ds_temp,
                       (uint64_t)now);
            http_post(json_buf);
            last_post_ms = now;
        }

        vTaskDelay(pdMS_TO_TICKS(SENSOR_READ_MS));
    }
}
