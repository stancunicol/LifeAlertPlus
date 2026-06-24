# Imagine Docker pentru microserviciul AI (FastAPI + Random Forest) — Python 3.11 "slim" (fără pachete inutile, imagine mai mică)
FROM python:3.11-slim

WORKDIR /app

# Copiem doar requirements.txt și instalăm dependențele înainte de a copia codul sursă —
# layer-ul de instalare pip e cache-uit de Docker și nu se reface decât dacă se schimbă requirements.txt,
# nu la fiecare modificare de cod Python (aceeași strategie de cache ca la Dockerfile-urile .NET).
COPY ai-service/requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY ai-service/*.py .              # Codul sursă (main.py) — fără subfolderul colab/ (nu e necesar la runtime, doar pentru antrenare)
COPY ai-service/models/ ./models/   # Artefactul .joblib pre-antrenat (model + scaler + label encoder)

# Calea către folderul cu modelul și numele exact al fișierului .joblib — pot fi suprascrise la rulare (docker run -e)
# fără a reconstrui imaginea, dacă se schimbă numele artefactului antrenat
ENV MODEL_DIR=/app/models
ENV ARTIFACT_FILE="patient_monitor_artifacts (3) (2).joblib"

EXPOSE 8000

# Pornește serverul ASGI Uvicorn, ascultând pe toate interfețele (0.0.0.0) — necesar ca alte containere
# (sau Azure Container Instances) să poată accesa serviciul din afara containerului
CMD ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "8000"]
