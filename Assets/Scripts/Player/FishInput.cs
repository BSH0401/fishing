using System;
using UnityEngine.InputSystem;

namespace FishGame.Player
{
    /// <summary>
    /// 신 Input System 액션을 코드로 정의한다.
    /// .inputactions 에셋 없이 동작하므로 프로젝트 셋업 실수가 없다.
    ///
    /// 기획서 2판 조작:
    ///   이동    WASD / 방향키 / 좌스틱
    ///   부스터  우클릭 (또는 Space / 게임패드 A)
    ///   청소기  좌클릭 홀드 (또는 E / 게임패드 X)
    /// </summary>
    public class FishInput : IDisposable
    {
        public InputAction Move    { get; }
        public InputAction Booster { get; }
        public InputAction Vacuum  { get; }
        public InputAction Pause   { get; }
        public InputAction Point   { get; }

        public FishInput()
        {
            // ── 이동 ────────────────────────────────────────────
            Move = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
            Move.AddCompositeBinding("2DVector")
                .With("Up",    "<Keyboard>/w")
                .With("Down",  "<Keyboard>/s")
                .With("Left",  "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            Move.AddCompositeBinding("2DVector")
                .With("Up",    "<Keyboard>/upArrow")
                .With("Down",  "<Keyboard>/downArrow")
                .With("Left",  "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");
            Move.AddBinding("<Gamepad>/leftStick");

            // ── 부스터 (우클릭) ─────────────────────────────────
            Booster = new InputAction("Booster", InputActionType.Button);
            Booster.AddBinding("<Mouse>/rightButton");
            Booster.AddBinding("<Keyboard>/space");
            Booster.AddBinding("<Gamepad>/buttonSouth");

            // ── 청소기 (좌클릭 홀드) ────────────────────────────
            Vacuum = new InputAction("Vacuum", InputActionType.Button);
            Vacuum.AddBinding("<Mouse>/leftButton");
            Vacuum.AddBinding("<Keyboard>/e");
            Vacuum.AddBinding("<Gamepad>/buttonWest");

            // ── 일시정지 ────────────────────────────────────────
            Pause = new InputAction("Pause", InputActionType.Button);
            Pause.AddBinding("<Keyboard>/escape");
            Pause.AddBinding("<Gamepad>/start");

            // ── 마우스 포인터 (마우스 조작 모드용) ──────────────
            Point = new InputAction("Point", InputActionType.Value, expectedControlType: "Vector2");
            Point.AddBinding("<Mouse>/position");
            Point.AddBinding("<Pen>/position");
            Point.AddBinding("<Touchscreen>/primaryTouch/position");
        }

        public void Enable()
        {
            Move.Enable(); Booster.Enable(); Vacuum.Enable(); Pause.Enable(); Point.Enable();
        }

        public void Disable()
        {
            Move.Disable(); Booster.Disable(); Vacuum.Disable(); Pause.Disable(); Point.Disable();
        }

        public void Dispose()
        {
            Move.Dispose(); Booster.Dispose(); Vacuum.Dispose(); Pause.Dispose(); Point.Dispose();
        }
    }
}
