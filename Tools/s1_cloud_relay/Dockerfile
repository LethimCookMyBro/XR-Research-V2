FROM python:3.11-slim
ENV PYTHONUNBUFFERED=1 PYTHONDONTWRITEBYTECODE=1 S1_RELAY_DATA_ROOT=/data
WORKDIR /app
COPY requirements.txt ./
RUN pip install --no-cache-dir -r requirements.txt
COPY receiver.py drive_sink.py ./
EXPOSE 8080
CMD ["python","receiver.py"]
