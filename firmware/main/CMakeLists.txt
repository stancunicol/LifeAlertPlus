idf_component_register(
    SRCS "main.cpp"
    INCLUDE_DIRS ""
    PRIV_REQUIRES json esp_http_server driver
                  efuse esp_wifi esp_event esp_http_client mbedtls
                  nvs_flash esp_timer lwip
)
