namespace FaceLocker.Models.Enums
{
    // ========= 反馈模式：开门反馈 vs 关门反馈 =========
    public enum FeedbackMode
    {
        None,
        /// <summary>
        /// 开门反馈：状态11表示打开，00表示关闭
        /// </summary>
        OpeningFeedback,

        /// <summary>
        /// 关门反馈：状态00表示打开，11表示关闭
        /// </summary>
        ClosingFeedback
    }
}
