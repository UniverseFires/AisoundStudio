"""Download the model files required by Aisound Studio from Hugging Face."""
import os
import pathlib
import zipfile
from huggingface_hub import hf_hub_download

APP = pathlib.Path(__file__).resolve().parents[1]
ROOT = pathlib.Path(os.environ.get("AISOUND_ROOT", str(APP / "AIEnvironment"))).resolve()
REPO_ID = "lj1995/VoiceConversionWebUI"
FILES = (
    ("hubert_base/config.json", "RVC"),
    ("hubert_base/preprocessor_config.json", "RVC"),
    ("hubert_base/pytorch_model.bin", "RVC"),
    ("rmvpe.pt", "RVC"),
    ("pretrained_v2/f0G40k.pth", "RVC"),
    ("pretrained_v2/f0D40k.pth", "RVC"),
    ("pymss_weights/model_bs_roformer_ep_368_sdr_12.9628.ckpt", "RVC"),
    ("pymss_weights/model_bs_roformer_ep_368_sdr_12.9628.yaml", "RVC"),
    ("pymss_weights/dereverb_mel_band_roformer_less_aggressive_anvuew_sdr_18.8050.ckpt", "RVC"),
    ("pymss_weights/dereverb_mel_band_roformer_anvuew.yaml", "RVC"),
    ("mute.zip", "downloads"),
)

def download(filename, destination):
    target = ROOT / destination
    target.mkdir(parents=True, exist_ok=True)
    print(f"Downloading {filename} from https://huggingface.co/{REPO_ID}", flush=True)
    path = hf_hub_download(repo_id=REPO_ID, filename=filename, local_dir=target)
    if filename == "rmvpe.pt":
        final = ROOT / "RVC/assets/rmvpe/rmvpe.pt"
    else:
        final = target / filename
    final.parent.mkdir(parents=True, exist_ok=True)
    if pathlib.Path(path).resolve() != final.resolve():
        final.write_bytes(pathlib.Path(path).read_bytes())
        pathlib.Path(path).unlink()
    print(f"Ready: {final.relative_to(ROOT)}", flush=True)
    return final

def extract_mute(archive_path):
    destination = (ROOT / "RVC/logs").resolve()
    destination.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(archive_path) as archive:
        if len(archive.infolist()) > 10000 or sum(item.file_size for item in archive.infolist()) > 1024**3:
            raise ValueError("Mute archive exceeds extraction limits.")
        for item in archive.infolist():
            output = (destination / item.filename).resolve()
            if not output.is_relative_to(destination):
                raise ValueError("Unsafe path in mute archive.")
        archive.extractall(destination)

if __name__ == "__main__":
    downloads = [download(name, target) for name, target in FILES]
    extract_mute(ROOT / "downloads/mute.zip")
