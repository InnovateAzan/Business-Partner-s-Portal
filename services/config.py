import os

from dotenv import load_dotenv


load_dotenv()


REQUIRED_ENV_VARS = [
    "DB_HOST",
    "DB_PORT",
    "DB_NAME",
    "DB_USER",
    "DB_PASSWORD",
    "API_BASE_URL",
]


def get_required_env(name):
    value = os.getenv(name)

    if value is None or not value.strip():
        missing = ", ".join(REQUIRED_ENV_VARS)
        raise RuntimeError(
            f"Missing required environment variable: {name}. "
            f"Set it in the project .env file. Required variables: {missing}"
        )

    return value.strip()


DB_HOST = get_required_env("DB_HOST")
DB_PORT = get_required_env("DB_PORT")
DB_NAME = get_required_env("DB_NAME")
DB_USER = get_required_env("DB_USER")
DB_PASSWORD = get_required_env("DB_PASSWORD")
API_BASE_URL = get_required_env("API_BASE_URL").rstrip("/")

API_USE_PROXY = os.getenv("API_USE_PROXY", "false").strip().lower() in {
    "1",
    "true",
    "yes",
    "on",
}

API_VERIFY_SSL = os.getenv("API_VERIFY_SSL", "false").strip().lower() in {
    "1",
    "true",
    "yes",
    "on",
}

API_CA_BUNDLE = os.getenv("API_CA_BUNDLE")
