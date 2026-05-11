FROM python:3.11-slim

WORKDIR /app

COPY ai-service/requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY ai-service/*.py .
COPY LifeAlertPlus/LifeAlertPlus.API/AIFiles/ ./models/

ENV MODEL_DIR=/app/models
ENV ARTIFACT_FILE="patient_monitor_artifacts (3).joblib"

EXPOSE 8000

CMD ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "8000"]
