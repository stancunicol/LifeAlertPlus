# Înregistrează componenta "main" (firmware-ul efectiv) la ESP-IDF, declarând explicit
# componentele de sistem de care depinde — fără ele în PRIV_REQUIRES, header-ele/funcțiile
# corespunzătoare nu ar fi vizibile la compilare (json=cJSON, driver=I2C/UART/GPIO,
# efuse=citire MAC unic, esp_wifi/esp_event=stack WiFi, esp_http_client=cereri către API,
# mbedtls=HTTPS + HMAC-SHA256, nvs_flash=stocare persistentă, esp_timer=callback-uri temporizate)
idf_component_register(
    SRCS "main.cpp"
    INCLUDE_DIRS ""
    PRIV_REQUIRES json esp_http_server driver
                  efuse esp_wifi esp_event esp_http_client mbedtls
                  nvs_flash esp_timer lwip esp_system
)
