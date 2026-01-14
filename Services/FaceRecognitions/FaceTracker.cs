using System;
using System.Collections.Generic;
using static FaceLocker.Services.BaiduFaceSDKInterop;

namespace FaceLocker.Services.FaceRecognitions
{
    /// <summary>
    /// 人脸追踪器 - 使用卡尔曼滤波预测人脸位置，实现平滑跟踪
    /// </summary>
    public class FaceTracker
    {
        // 卡尔曼滤波状态
        private float _x, _y, _w, _h;           // 当前位置和大小
        private float _vx, _vy, _vw, _vh;       // 速度
        private float _score;                    // 置信度
        private DateTime _lastUpdateTime;        // 上次更新时间
        private bool _hasValidState = false;     // 是否有有效状态
        
        // 卡尔曼滤波参数 - 优化版本，更快响应
        private const float PROCESS_NOISE = 0.05f;     // 降低过程噪声，更信任预测
        private const float MEASUREMENT_NOISE = 0.3f;  // 降低测量噪声，更快跟随检测结果
        private const float VELOCITY_DECAY = 0.85f;    // 加快速度衰减，减少惯性
        private const float MAX_PREDICTION_TIME = 0.15f; // 缩短最大预测时间到150ms
        private const float MIN_SCORE_THRESHOLD = 0.3f; // 降低置信度阈值，更容易显示
        
        // 协方差矩阵（简化版）
        private float _px, _py, _pw, _ph;       // 位置协方差
        private float _pvx, _pvy, _pvw, _pvh;   // 速度协方差

        /// <summary>
        /// 使用新的检测结果更新追踪器
        /// </summary>
        public void Update(WrapperFaceBox[] detectedFaces)
        {
            var now = DateTime.Now;
            
            if (detectedFaces == null || detectedFaces.Length == 0)
            {
                // 没有检测到人脸，保持预测状态但降低置信度
                if (_hasValidState)
                {
                    _score *= 0.9f; // 快速降低置信度
                    if (_score < MIN_SCORE_THRESHOLD)
                    {
                        _hasValidState = false;
                    }
                }
                return;
            }

            // 找到置信度最高的人脸
            var bestFace = detectedFaces[0];
            foreach (var face in detectedFaces)
            {
                if (face.score > bestFace.score)
                {
                    bestFace = face;
                }
            }

            // 计算人脸框的中心和大小
            float measuredX = bestFace.center_x;
            float measuredY = bestFace.center_y;
            float measuredW = bestFace.width;
            float measuredH = bestFace.height;

            if (!_hasValidState)
            {
                // 第一次检测，直接初始化
                _x = measuredX;
                _y = measuredY;
                _w = measuredW;
                _h = measuredH;
                _vx = _vy = _vw = _vh = 0;
                _px = _py = _pw = _ph = 1.0f;
                _pvx = _pvy = _pvw = _pvh = 1.0f;
                _score = bestFace.score;
                _hasValidState = true;
            }
            else
            {
                // 卡尔曼滤波更新
                float dt = (float)(now - _lastUpdateTime).TotalSeconds;
                dt = Math.Min(dt, MAX_PREDICTION_TIME); // 限制时间步长

                // 预测步骤
                float predictedX = _x + _vx * dt;
                float predictedY = _y + _vy * dt;
                float predictedW = _w + _vw * dt;
                float predictedH = _h + _vh * dt;

                // 更新协方差（预测）
                _px += _pvx * dt * dt + PROCESS_NOISE;
                _py += _pvy * dt * dt + PROCESS_NOISE;
                _pw += _pvw * dt * dt + PROCESS_NOISE;
                _ph += _pvh * dt * dt + PROCESS_NOISE;

                // 计算卡尔曼增益
                float kx = _px / (_px + MEASUREMENT_NOISE);
                float ky = _py / (_py + MEASUREMENT_NOISE);
                float kw = _pw / (_pw + MEASUREMENT_NOISE);
                float kh = _ph / (_ph + MEASUREMENT_NOISE);

                // 更新状态
                float innovationX = measuredX - predictedX;
                float innovationY = measuredY - predictedY;
                float innovationW = measuredW - predictedW;
                float innovationH = measuredH - predictedH;

                _x = predictedX + kx * innovationX;
                _y = predictedY + ky * innovationY;
                _w = predictedW + kw * innovationW;
                _h = predictedH + kh * innovationH;

                // 更新速度估计 - 使用更大的权重快速响应运动
                if (dt > 0.001f)
                {
                    float velocityWeight = 0.6f; // 60%权重给新速度，快速响应
                    _vx = _vx * (1 - velocityWeight) + velocityWeight * (innovationX / dt);
                    _vy = _vy * (1 - velocityWeight) + velocityWeight * (innovationY / dt);
                    _vw = _vw * VELOCITY_DECAY + (1 - VELOCITY_DECAY) * (innovationW / dt);
                    _vh = _vh * VELOCITY_DECAY + (1 - VELOCITY_DECAY) * (innovationH / dt);
                }

                // 更新协方差（修正）
                _px *= (1 - kx);
                _py *= (1 - ky);
                _pw *= (1 - kw);
                _ph *= (1 - kh);

                _score = bestFace.score;
            }

            _lastUpdateTime = now;
        }

        /// <summary>
        /// 获取当前预测的人脸位置（每帧调用）
        /// </summary>
        public WrapperFaceBox[] GetPredictedFaces()
        {
            if (!_hasValidState || _score < MIN_SCORE_THRESHOLD)
            {
                return Array.Empty<WrapperFaceBox>();
            }

            var now = DateTime.Now;
            float dt = (float)(now - _lastUpdateTime).TotalSeconds;
            
            // 限制预测时间，防止人脸框飘走
            if (dt > MAX_PREDICTION_TIME)
            {
                _hasValidState = false;
                return Array.Empty<WrapperFaceBox>();
            }

            // 使用速度预测当前位置
            float predictedX = _x + _vx * dt;
            float predictedY = _y + _vy * dt;
            float predictedW = Math.Max(10, _w + _vw * dt); // 防止宽度变成负数
            float predictedH = Math.Max(10, _h + _vh * dt); // 防止高度变成负数

            return new WrapperFaceBox[]
            {
                new WrapperFaceBox
                {
                    center_x = predictedX,
                    center_y = predictedY,
                    width = predictedW,
                    height = predictedH,
                    score = _score * (1 - dt / MAX_PREDICTION_TIME * 0.3f) // 随时间降低置信度显示
                }
            };
        }

        /// <summary>
        /// 重置追踪器状态
        /// </summary>
        public void Reset()
        {
            _hasValidState = false;
            _score = 0;
            _vx = _vy = _vw = _vh = 0;
        }

        /// <summary>
        /// 是否有有效的追踪状态
        /// </summary>
        public bool HasValidState => _hasValidState && _score >= MIN_SCORE_THRESHOLD;
    }
}
