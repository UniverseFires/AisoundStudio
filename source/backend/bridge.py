"""Aisound Studio bridge: real RVC execution, JSON events, no simulated AI."""
from __future__ import annotations
import contextlib
import importlib.metadata
import json
import os
import pathlib
import re
import shutil
import subprocess
import sys
import time
import traceback
import uuid
import zipfile

APP = pathlib.Path(__file__).resolve().parents[1]
ROOT = pathlib.Path(os.environ.get('AISOUND_ROOT', str(APP / 'AIEnvironment'))).resolve()
RVC = ROOT / 'RVC'
DATA = APP / 'data'
DATA.mkdir(parents=True, exist_ok=True)
PROTOCOL = sys.stdout
NO_WINDOW = getattr(subprocess, 'CREATE_NO_WINDOW', 0)

def emit(kind, **values):
    PROTOCOL.write(json.dumps({'type': kind, **values}, ensure_ascii=False) + '\n')
    PROTOCOL.flush()

def progress(value, message):
    emit('progress', value=value, message=message)

def env():
    result = os.environ.copy()
    result.update(PYTHONUTF8='1', PYTHONIOENCODING='utf-8',
                  weight_root=str(RVC / 'assets/weights'),
                  weight_pymss_root=str(RVC / 'assets/pymss_weights'),
                  index_root=str(RVC / 'logs'), outside_index_root=str(RVC / 'assets/indices'),
                  rmvpe_root=str(RVC / 'assets/rmvpe'),
                  HF_HOME=str(ROOT / 'downloads/hf-cache'), HF_ENDPOINT='https://hf-mirror.com',
                  PYMSS_MODEL_DIR=str(RVC / 'assets/pymss_weights'))
    result['PATH'] = str(RVC) + os.pathsep + result.get('PATH', '')
    result['PYTHONPATH'] = str(RVC) + os.pathsep + result.get('PYTHONPATH', '')
    return result

def configure():
    os.environ.update(env())
    os.chdir(RVC)
    sys.path.insert(0, str(RVC))
    sys.argv = ['aisound-worker']  # RVC Config parses argv globally.
    for folder in ('assets/weights', 'assets/indices', 'logs'):
        (RVC / folder).mkdir(parents=True, exist_ok=True)
    binary = pathlib.Path(ffmpeg())
    destination = RVC / 'ffmpeg.exe'
    if binary.resolve() != destination.resolve():
        shutil.copy2(binary, destination)

def ffmpeg():
    local = RVC / 'ffmpeg.exe'
    if local.exists() and local.stat().st_size > 1000000:
        return str(local)
    try:
        import imageio_ffmpeg
        return imageio_ffmpeg.get_ffmpeg_exe()
    except ImportError:
        found = shutil.which('ffmpeg')
        if found:
            return found
        raise RuntimeError('缺少 FFmpeg，请到“环境与设置”补齐音频工具。')

def run(args, stage='', epoch_total=0):
    process = subprocess.Popen([str(x) for x in args], cwd=RVC, env=env(),
                               stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                               text=True, encoding='utf-8', errors='replace',
                               creationflags=NO_WINDOW)
    last = []
    for line in process.stdout:
        line = line.strip()
        if not line:
            continue
        last.append(line)
        last = last[-12:]
        emit('log', message=line)
        if epoch_total:
            match = re.search(r'(?:Train Epoch:|Epoch[:\s]+|轮次[:：]?)\s*(\d+)', line, re.I)
            if match:
                epoch = min(int(match[1]), epoch_total)
                progress(38 + 48 * epoch / epoch_total, f'训练音色 · 第 {epoch}/{epoch_total} 轮')
    process.stdout.close()
    if process.wait() != 0:
        detail = '\n'.join(last[-6:])
        if 'out of memory' in detail.lower():
            detail = '显存不足。训练时将批次改为 1，关闭其他占显存的应用。\n' + detail
        raise RuntimeError(f'{stage or "处理"}失败：\n{detail}')

def audio_file(value):
    path = pathlib.Path(value).resolve()
    if not path.is_file():
        raise ValueError('音频文件不存在：' + str(path))
    if path.suffix.lower() not in ('.mp3', '.wav', '.flac', '.m4a', '.aac', '.ogg', '.wma'):
        raise ValueError('不支持的音频格式：' + path.suffix)
    return path

def out_dir(request, prefix):
    base = pathlib.Path(request.get('output_dir') or APP / 'exports').resolve()
    base.mkdir(parents=True, exist_ok=True)
    clean = re.sub(r'[<>:"/\\|?*\x00-\x1f]', '_', prefix).strip(' .')[:70] or 'audio'
    target = base / f'{clean}_{time.strftime("%Y%m%d_%H%M%S")}_{uuid.uuid4().hex[:4]}'
    target.mkdir()
    return target

def mp3(source, destination, bitrate=320):
    run([ffmpeg(), '-hide_banner', '-nostdin', '-n', '-i', source,
         '-vn', '-codec:a', 'libmp3lame', '-b:a', f'{bitrate}k', destination], 'MP3 导出')

def find_audio(folder):
    files = sorted(pathlib.Path(folder).rglob('*.wav'))
    if len(files) != 1:
        raise RuntimeError(f'分离引擎应生成一份音轨，实际找到 {len(files)} 份：{folder}')
    return files[0]

def clean_intermediates(target):
    target = pathlib.Path(target).resolve()
    for name in ('raw_vocals','raw_instrumental','clean_vocals','removed_reverb'):
        path = (target/name).resolve()
        if path.is_relative_to(target) and path.is_dir():
            shutil.rmtree(path)

def model_file(folder, name, expected):
    file = RVC / folder / name
    return file.exists() and file.stat().st_size == expected

def diagnostics():
    packages = {}
    for name in ('torch', 'torchaudio', 'librosa', 'soundfile', 'pymss', 'pymss-core',
                 'transformers', 'numpy', 'faiss-cpu', 'scipy', 'praat-parselmouth'):
        try:
            packages[name] = importlib.metadata.version(name)
        except importlib.metadata.PackageNotFoundError:
            packages[name] = None
    checks = []
    common = all(packages.values())
    checks.append({'name': 'AI 运行依赖', 'ok': common,
                   'detail': '已安装' if common else '缺少：' + '、'.join(k for k,v in packages.items() if not v)})
    try:
        tool = ffmpeg()
        subprocess.run([tool, '-version'], check=True, capture_output=True, creationflags=NO_WINDOW)
        audio_ready = True
        checks.append({'name': 'MP3 编码与音频处理', 'ok': True, 'detail': 'FFmpeg 已就绪'})
    except Exception:
        audio_ready = False
        checks.append({'name': 'MP3 编码与音频处理', 'ok': False, 'detail': '需要安装 FFmpeg'})
    gpu = False
    gpu_name = '尚未检测'
    if packages['torch']:
        try:
            import torch
            if torch.cuda.is_available():
                gpu_name = torch.cuda.get_device_name(0)
                with torch.inference_mode():
                    x = torch.ones((32,32), device='cuda')
                    gpu = bool(torch.isfinite(x @ x).all().item())
                del x
                torch.cuda.empty_cache()
        except Exception as exc:
            gpu_name = str(exc)[:120]
    checks.append({'name': 'NVIDIA 显卡加速', 'ok': gpu, 'detail': gpu_name if gpu else 'CUDA 未就绪 · ' + gpu_name})
    vocals = (model_file('assets/pymss_weights', 'model_bs_roformer_ep_368_sdr_12.9628.ckpt',319821424)
              and model_file('assets/pymss_weights','model_bs_roformer_ep_368_sdr_12.9628.yaml',2273))
    dereverb = (model_file('assets/pymss_weights','dereverb_mel_band_roformer_less_aggressive_anvuew_sdr_18.8050.ckpt',456754215)
                and model_file('assets/pymss_weights','dereverb_mel_band_roformer_anvuew.yaml',1846))
    hubert = (model_file('assets/hubert_base','pytorch_model.bin',189206711)
              and model_file('assets/hubert_base','config.json',1492)
              and model_file('assets/hubert_base','preprocessor_config.json',225))
    pitch = model_file('assets/rmvpe','rmvpe.pt',181184272)
    pretrained = model_file('assets/pretrained_v2','f0G40k.pth',73106273) and model_file('assets/pretrained_v2','f0D40k.pth',142875703)
    for name, ok, detail in [('人声分离模型',vocals,'BS-RoFormer'),('去混响模型',dereverb,'Mel-Band RoFormer'),
                             ('音色语义与音高模型',hubert and pitch,'HuBERT + RMVPE'),('音色训练底座',pretrained,'RVC v2 · 40k')]:
        checks.append({'name':name,'ok':ok,'detail':detail + (' · 已就绪' if ok else ' · 待下载')})
    split_ready = common and gpu and audio_ready and vocals
    convert_ready = common and gpu and audio_ready and hubert and pitch
    result = {'checks':checks,'gpu':gpu_name,'audio_ready':audio_ready,'split_ready':split_ready,
              'train_ready':convert_ready and pretrained,'cover_ready':split_ready and convert_ready,
              'convert_ready':convert_ready, 'dereverb_ready':dereverb,'packages':packages}
    emit('result', data=result)

def separation(source, target, dereverb=False, start=0, end=70):
    from tools.pymss_webui import pymss_separate
    vocal_dir, inst_dir = target / 'raw_vocals', target / 'raw_instrumental'
    errors = []
    def callback(event):
        if event.get('event') == 'fatal':
            errors.append(event.get('message','分离失败'))
        if event.get('event') == 'progress':
            done = event.get('done', 0) / max(event.get('total', 1), 1)
            if isinstance(done,(int,float)) and 0 <= done <= 1:
                progress(start + (end-start)*done, '正在分离人声与伴奏')
        elif event.get('message'):
            emit('log', message=event['message'])
    progress(start,'加载人声分离模型')
    for _ in pymss_separate('vocals-bs-roformer-368','',str(vocal_dir),[str(source)],str(inst_dir),'wav',callback):
        pass
    if errors:
        raise RuntimeError(errors[-1])
    vocals, instrumental = find_audio(vocal_dir), find_audio(inst_dir)
    if dereverb:
        progress(end,'清理人声混响')
        clean, removed = target / 'clean_vocals', target / 'removed_reverb'
        errors.clear()
        for _ in pymss_separate('dereverb-less-aggressive-18.8050','',str(clean),[str(vocals)],str(removed),'wav',callback):
            pass
        if errors:
            raise RuntimeError(errors[-1])
        vocals = find_audio(clean)
    return vocals, instrumental

def split(request):
    source = audio_file(request['input'])
    target = out_dir(request, source.stem + '_分离')
    vocals, instrumental = separation(source,target,bool(request.get('dereverb')))
    progress(82,'导出人声 MP3')
    mp3(vocals,target/'人声.mp3',int(request.get('bitrate',320)))
    progress(92,'导出伴奏 MP3')
    mp3(instrumental,target/'伴奏.mp3',int(request.get('bitrate',320)))
    outputs = [str(target/'人声.mp3'),str(target/'伴奏.mp3')]
    if request.get('keep_wav',True):
        shutil.copy2(vocals,target/'人声.wav')
        shutil.copy2(instrumental,target/'伴奏.wav')
        outputs += [str(target/'人声.wav'),str(target/'伴奏.wav')]
    clean_intermediates(target)
    progress(100,'人声与伴奏已导出')
    emit('result',data={'folder':str(target),'outputs':outputs,'vocals':str(target/'人声.mp3')})

def voices():
    catalog = DATA/'voices.json'
    data = json.loads(catalog.read_text(encoding='utf-8')) if catalog.exists() else []
    return [v for v in data if pathlib.Path(v['model']).is_file()]

def save_voice(voice):
    data = voices()
    data = [v for v in data if v['id'] != voice['id']]
    data.append(voice)
    temp = DATA/'voices.json.tmp'
    temp.write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
    temp.replace(DATA/'voices.json')

def checkpoint(path):
    import torch
    value = torch.load(path,map_location='cpu',weights_only=True)
    if not isinstance(value,dict) or 'weight' not in value or 'config' not in value:
        raise ValueError('请选择可推理的 RVC .pth 音色模型；训练用 G/D 检查点不能直接翻唱。')
    if len(value['config']) != 18 or 'emb_g.weight' not in value['weight']:
        raise ValueError('音色文件结构与 RVC 不兼容。')
    return value

def import_voice(request):
    source = pathlib.Path(request['model']).resolve()
    if source.suffix.lower() != '.pth' or not source.is_file():
        raise ValueError('请选择 .pth 音色文件。')
    meta = checkpoint(source)
    name = (request.get('name') or source.stem).strip()
    if (not name or len(name)>64 or name in ('.','..') or name.endswith('.')
            or re.search(r'[<>:"/\\|?*\x00-\x1f]',name) or pathlib.PureWindowsPath(name).is_reserved()):
        raise ValueError('请输入可作为 Windows 文件夹名称的音色名称。')
    folder = APP/'voices'/name
    if folder.exists():
        raise ValueError('该音色文件夹已存在，请使用其他名称。')
    candidate = request.get('index','')
    if candidate and (not pathlib.Path(candidate).is_file() or pathlib.Path(candidate).suffix.lower()!='.index'):
        raise ValueError('索引文件无效。')
    folder.mkdir(parents=True)
    model = folder/(name+'.pth')
    shutil.copy2(source,model)
    index = ''
    if candidate:
        index_file = folder/(name+'.index')
        shutil.copy2(candidate,index_file)
        index = str(index_file)
    voice = {'id':'voice_'+uuid.uuid4().hex[:12], 'name':name,'model':str(model),'index':index,
             'created':time.strftime('%Y-%m-%d %H:%M'),'source':'导入','version':meta.get('version','v1'),
             'sample_rate':str(meta.get('sr',meta['config'][-1]))}
    save_voice(voice)
    emit('result',data={'voice':voice,'outputs':[str(model)],'folder':str(folder)})

def train(request):
    sources = [audio_file(x) for x in request['inputs']]
    if not sources:
        raise ValueError('请至少导入一份干净独唱。')
    epochs = int(request.get('epochs',200))
    batch = int(request.get('batch_size',2))
    if not 10 <= epochs <= 1000 or not 1 <= batch <= 4:
        raise ValueError('训练参数超出支持范围。')
    identity = 'voice_'+time.strftime('%Y%m%d_%H%M%S')+'_'+uuid.uuid4().hex[:6]
    dataset = APP/'datasets'/identity
    dataset.mkdir(parents=True)
    for i, source in enumerate(sources):
        # Use stable ASCII names; downstream RVC keys split at periods.
        shutil.copy2(source,dataset/f'{i:04d}{source.suffix.lower()}')
    experiment = RVC/'logs'/identity
    experiment.mkdir()
    mute = RVC/'logs/mute'
    archive = ROOT/'downloads/mute.zip'
    if not mute.exists() and archive.exists():
        with zipfile.ZipFile(archive) as z:
            base = (RVC/'logs').resolve()
            if any(not (base/i.filename).resolve().is_relative_to(base) for i in z.infolist()):
                raise ValueError('训练静音归档路径异常。')
            z.extractall(base)
    if not (mute/'0_gt_wavs/mute40k.wav').is_file():
        raise RuntimeError('缺少训练静音样本，请先完成环境安装。')
    progress(5,'整理训练音频并切分')
    run([sys.executable,'-u','-m','train.preprocess',dataset,'40000','4',experiment,'False','3.7'],'音频预处理')
    gt = experiment/'0_gt_wavs'
    if not list(gt.glob('*.wav')):
        raise RuntimeError('没有提取到有效人声片段，请检查素材是否为清晰独唱。')
    progress(18,'提取音高 · RMVPE')
    run([sys.executable,'-u','-m','train.dataset.extract_f0','cuda','1','0','0',experiment,'True'],'音高提取')
    progress(28,'提取音色语义特征 · HuBERT')
    run([sys.executable,'-u','-m','train.dataset.extract_hubert_feature','cuda:0','1','0','0',experiment,'v2','True'],'语义特征提取')
    feature = experiment/'3_feature768'
    f0, nsf = experiment/'2a_f0', experiment/'2b-f0nsf'
    keys = [p.stem for p in gt.glob('*.wav') if (feature/(p.stem+'.npy')).is_file()
            and (f0/(p.name+'.npy')).is_file() and (nsf/(p.name+'.npy')).is_file()]
    if not keys:
        raise RuntimeError('音高或语义特征未成功生成；查看任务日志。')
    lines = ['|'.join([str(gt/(key+'.wav')),str(feature/(key+'.npy')),
                       str(f0/(key+'.wav.npy')),str(nsf/(key+'.wav.npy')),'0']) for key in keys]
    silent = '|'.join([str(mute/'0_gt_wavs/mute40k.wav'),str(mute/'3_feature768/mute.npy'),
                       str(mute/'2a_f0/mute.wav.npy'),str(mute/'2b-f0nsf/mute.wav.npy'),'0'])
    lines += [silent,silent]
    (experiment/'filelist.txt').write_text('\n'.join(lines),encoding='utf-8')
    config = json.loads((RVC/'configs/v1/40k.json').read_text(encoding='utf-8'))
    (experiment/'config.json').write_text(json.dumps(config,indent=2),encoding='utf-8')
    progress(38,'开始训练音色')
    run([sys.executable,'-u','-m','train.train','-e',identity,'-sr','40k','-f0','1','-bs',str(batch),
         '-g','0','-te',str(epochs),'-se','10','-pg','assets/pretrained_v2/f0G40k.pth',
         '-pd','assets/pretrained_v2/f0D40k.pth','-l','1','-c','0','-sw','1','-v','v2'],
        '音色训练',epochs)
    model = RVC/'assets/weights'/f'{identity}.pth'
    saved = list((RVC/'assets/weights').glob(f'{identity}_e*_s*.pth'))
    if saved:
        def model_epoch(path):
            match = re.search(r'_e(\d+)_s(\d+)\.pth$', path.name)
            return (int(match[1]), int(match[2])) if match else (-1, -1)
        shutil.copy2(max(saved,key=model_epoch),model)
    if not model.is_file():
        raise RuntimeError('训练进程结束，但未找到可用音色模型。')
    checkpoint(model)
    progress(90,'生成音色检索索引')
    run([sys.executable,'-u','-m','train.train_index',identity,'v2',RVC/'assets/indices','4','single'],'索引生成')
    indices = list((RVC/'assets/indices').glob(f'*{identity}*.index'))
    if not indices:
        indices = list(experiment.glob('added*.index'))
    if not indices:
        raise RuntimeError('训练已保存音色，但索引未生成；日志中的模型可在音色库手动导入。')
    index_file = RVC/'assets/indices'/f'{identity}.index'
    if indices[0].resolve() != index_file.resolve():
        shutil.copy2(indices[0],index_file)
    voice = {'id':identity,'name':request['name'].strip(),'model':str(model),'index':str(index_file),
             'created':time.strftime('%Y-%m-%d %H:%M'),'source':'训练','version':'v2','sample_rate':'40k'}
    save_voice(voice)
    progress(100,'音色已保存并加入音色库')
    emit('result',data={'voice':voice,'outputs':[str(model),str(index_file)],'folder':str(model.parent)})

def convert_vocals(source, model, index, request, destination):
    """Convert bounded overlapping chunks instead of extracting pitch for a whole song."""
    import gc
    import math
    import tempfile
    import numpy as np
    import soundfile as sf

    # Long, variable-length offline jobs do not benefit from retaining captured
    # CUDA graphs. Set this before importing Config, which otherwise enables them.
    os.environ['RVC_CUDA_GRAPH'] = '0'
    os.environ['weight_root'] = str(pathlib.Path(model).resolve().parent)
    checkpoint(model)
    progress(53, '加载音色转换模型 · 分段节省显存模式')
    import torch
    import faiss
    from configs.config import Config
    from infer.vc.modules import VC

    workers = max(1, min(4, os.cpu_count() or 1))
    torch.set_num_threads(workers)
    faiss.omp_set_num_threads(workers)
    config = Config()
    voice = VC(config)
    voice.get_vc(pathlib.Path(model).name)
    destination = pathlib.Path(destination)
    input_rate = 16000
    chunk_frames = 8 * input_rate
    overlap_frames = input_rate // 4
    step_frames = chunk_frames - overlap_frames
    output_rate = int(voice.tgt_sr)
    output_overlap = round(overlap_frames * output_rate / input_rate)

    # Streaming decode and output keep both RAM and GPU memory bounded. The
    # finished destination appears only after every chunk has succeeded.
    with tempfile.TemporaryDirectory(prefix='.voice-chunks-', dir=destination.parent) as work:
        work = pathlib.Path(work)
        decoded = work / 'input-16k.wav'
        run([ffmpeg(), '-hide_banner', '-loglevel', 'error', '-nostdin', '-n',
             '-i', source, '-vn', '-ac', '1', '-ar', str(input_rate),
             '-c:a', 'pcm_s16le', decoded], '准备分段人声')
        staged_output = work / 'converted.wav'
        with sf.SoundFile(decoded) as reader:
            total_frames = len(reader)
            if total_frames == 0:
                raise RuntimeError('没有读取到有效人声音频。')
            count = max(1, math.ceil(max(0, total_frames - overlap_frames) / step_frames))
            emit('log', message=f'节省显存模式：{total_frames / input_rate:.2f} 秒人声，'
                 f'{count} 段，每段最多 8 秒，重叠 0.25 秒；CUDA Graph 已关闭。')
            pending = None
            written = 0
            with sf.SoundFile(staged_output, mode='w', samplerate=output_rate,
                              channels=1, subtype='PCM_24') as writer:
                for number in range(count):
                    start = number * step_frames
                    end = min(start + chunk_frames, total_frames)
                    progress(55 + 26 * start / total_frames,
                             f'音色转换 · 第 {number + 1}/{count} 段'
                             f'（{start / input_rate:.0f}/{total_frames / input_rate:.0f} 秒）')
                    reader.seek(start)
                    chunk = reader.read(end - start, dtype='float32')
                    chunk_file = work / 'chunk.wav'
                    sf.write(chunk_file, chunk, input_rate, subtype='PCM_16')
                    info, result = voice.vc_single(
                        int(request.get('speaker_id', 0)), str(chunk_file),
                        int(request.get('pitch', 0)), 'rmvpe', index,
                        float(request.get('index_rate', 0.65)), 0,
                        0.25, float(request.get('protect', 0.33)))
                    if not result or result[0] is None or result[1] is None:
                        detail = str(info)
                        if 'out of memory' in detail.lower():
                            raise RuntimeError('音色转换显存不足，请先关闭其他占用显卡的软件后重试。'
                                               f'\n失败于第 {number + 1}/{count} 段。\n{detail}')
                        raise RuntimeError(f'第 {number + 1}/{count} 段音色转换失败：\n{detail}')
                    if int(result[0]) != output_rate:
                        raise RuntimeError('音色转换过程中采样率发生变化，已停止导出。')
                    raw = np.asarray(result[1])
                    samples = raw.astype(np.float32)
                    if np.issubdtype(raw.dtype, np.integer):
                        samples /= float(max(abs(np.iinfo(raw.dtype).min), np.iinfo(raw.dtype).max))
                    if samples.ndim != 1 or samples.size == 0 or not np.isfinite(samples).all():
                        raise RuntimeError(f'第 {number + 1}/{count} 段返回了无效音频。')
                    expected = round((end - start) * output_rate / input_rate)
                    # RVC works on 10 ms frames; compensate only sub-frame
                    # rounding so the full song keeps its original timing.
                    if abs(len(samples) - expected) > math.ceil(output_rate * 0.02):
                        raise RuntimeError(f'第 {number + 1}/{count} 段音频长度异常，已停止导出。')
                    if len(samples) < expected:
                        samples = np.pad(samples, (0, expected - len(samples)), mode='edge')
                    else:
                        samples = samples[:expected]
                    if pending is not None:
                        length = len(pending)
                        fade = np.linspace(0, 1, length, dtype=np.float32)
                        writer.write(pending * (1 - fade) + samples[:length] * fade)
                        written += length
                        samples = samples[length:]
                    if end < total_frames:
                        writer.write(samples[:-output_overlap])
                        written += len(samples) - output_overlap
                        pending = samples[-output_overlap:].copy()
                    else:
                        writer.write(samples)
                        written += len(samples)
                        pending = None
                    emit('log', message=f'第 {number + 1}/{count} 段完成。\n{info}')
                    del result, raw, samples, chunk
                    gc.collect()
                    if torch.cuda.is_available():
                        torch.cuda.empty_cache()
                    progress(55 + 26 * end / total_frames,
                             f'音色转换 · 已完成 {number + 1}/{count} 段')
                if written != round(total_frames * output_rate / input_rate):
                    raise RuntimeError('分段合成总长度不一致，已停止导出。')
        os.replace(staged_output, destination)

def cover(request):
    source = audio_file(request['input'])
    selected = next((v for v in voices() if v['id'] == request['voice_id']),None)
    if not selected:
        raise ValueError('请选择有效音色；可以训练或导入 .pth 模型。')
    target = out_dir(request,source.stem+'_'+selected['name']+'_翻唱')
    if request.get('vocal_only'):
        vocals, instrumental = source, None
    else:
        vocals, instrumental = separation(source,target,bool(request.get('dereverb')),0,45)
    progress(52,'将人声转换为 '+selected['name'])
    converted = target/'转换人声.wav'
    convert_vocals(vocals,selected['model'],selected.get('index',''),request,converted)
    progress(82,'导出翻唱音频')
    mp3(converted,target/'转换人声.mp3',int(request.get('bitrate',320)))
    outputs = [str(target/'转换人声.mp3')]
    if instrumental:
        gain_v = float(request.get('vocal_gain',1))
        gain_i = float(request.get('instrumental_gain',1))
        if not 0 <= gain_v <= 2 or not 0 <= gain_i <= 2:
            raise ValueError('混音音量须在 0–200% 范围内。')
        mix = target/'完整翻唱.wav'
        filt = f'[0:a]volume={gain_v}[v];[1:a]volume={gain_i}[i];[v][i]amix=inputs=2:duration=longest:normalize=0,alimiter=limit=0.95[out]'
        run([ffmpeg(),'-hide_banner','-nostdin','-n','-i',converted,'-i',instrumental,
             '-filter_complex',filt,'-map','[out]','-c:a','pcm_s24le',mix],'伴奏混音')
        mp3(mix,target/'完整翻唱.mp3',int(request.get('bitrate',320)))
        outputs.insert(0,str(target/'完整翻唱.mp3'))
        if request.get('keep_wav',True):
            outputs.append(str(mix))
    if request.get('keep_wav',True):
        outputs.append(str(converted))
    else:
        converted.unlink()
        master = target/'完整翻唱.wav'
        if master.is_file():
            master.unlink()
    clean_intermediates(target)
    progress(100,'翻唱完成，音频已导出')
    emit('result',data={'folder':str(target),'outputs':outputs})

def transcode(request):
    source = audio_file(request['input'])
    target = out_dir(request,source.stem+'_MP3')
    progress(20,'转换为 MP3')
    mp3(source,target/(source.stem+'.mp3'),int(request.get('bitrate',320)))
    progress(100,'MP3 已导出')
    emit('result',data={'folder':str(target),'outputs':[str(target/(source.stem+'.mp3'))]})

if __name__ == '__main__':
    action = sys.argv[1]
    request = json.loads(pathlib.Path(sys.argv[2]).read_text(encoding='utf-8-sig')) if len(sys.argv)>2 else {}
    try:
        if action == 'diagnostics':
            diagnostics()
        else:
            configure()
            # Keep legacy RVC prints off the JSON protocol stream.
            with contextlib.redirect_stdout(sys.stderr):
                {'split':split,'train':train,'cover':cover,'import_voice':import_voice,'transcode':transcode}[action](request)
    except Exception as exc:
        emit('error',message=str(exc))
        traceback.print_exc(file=sys.stderr)
        raise SystemExit(1)
