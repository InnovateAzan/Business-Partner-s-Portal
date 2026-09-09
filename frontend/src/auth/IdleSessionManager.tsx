import {
  useCallback,
  useEffect,
  useRef,
} from "react";

import { useLocation, useNavigate } from "react-router-dom";
import { useAuth } from "./AuthContext";

const IDLE_TIMEOUT_MS = 15 * 60 * 1000;

// Local storage is used so activity is shared
// between multiple tabs of the same portal.
const LAST_ACTIVITY_KEY =
  "bpp-last-user-activity";

const ACTIVITY_EVENTS: Array<keyof WindowEventMap> = [
  "mousedown",
  "keydown",
  "scroll",
  "touchstart",
];

export function IdleSessionManager() {
  const { user, logout } = useAuth();

  const navigate = useNavigate();
  const location = useLocation();

  const timeoutRef =
    useRef<number | null>(null);

  const lastRecordedActivityRef =
    useRef<number>(0);

  const performLogout =
    useCallback(() => {
      if (!user) {
        return;
      }

      if (timeoutRef.current !== null) {
        window.clearTimeout(
          timeoutRef.current
        );

        timeoutRef.current = null;
      }

      localStorage.removeItem(
        LAST_ACTIVITY_KEY
      );

      logout();

      navigate(
        "/login",
        {
          replace: true,
          state: {
            reason: "idle-timeout",
          },
        }
      );
    }, [logout, navigate, user]);

  const scheduleLogout =
    useCallback(
      (lastActivity: number) => {
        if (timeoutRef.current !== null) {
          window.clearTimeout(
            timeoutRef.current
          );
        }

        const elapsed =
          Date.now() - lastActivity;

        const remaining =
          IDLE_TIMEOUT_MS - elapsed;

        if (remaining <= 0) {
          performLogout();
          return;
        }

        timeoutRef.current =
          window.setTimeout(
            performLogout,
            remaining
          );
      },
      [performLogout]
    );

  const recordActivity =
    useCallback(() => {
      if (!user) {
        return;
      }

      const now = Date.now();

      /*
       * Mouse/scroll events bohat frequently fire hote hain.
       * LocalStorage ko har millisecond update karne ki
       * zarurat nahi.
       *
       * Maximum once every 5 seconds record karenge.
       */
      if (
        now -
          lastRecordedActivityRef.current <
        5000
      ) {
        return;
      }

      lastRecordedActivityRef.current =
        now;

      localStorage.setItem(
        LAST_ACTIVITY_KEY,
        String(now)
      );

      scheduleLogout(now);
    },
    [scheduleLogout, user]
  );

  useEffect(() => {
    if (!user) {
      if (timeoutRef.current !== null) {
        window.clearTimeout(
          timeoutRef.current
        );

        timeoutRef.current = null;
      }

      return;
    }

    /*
     * Existing last activity check karo.
     * Agar nahi hai to current time se session start.
     */
    const stored =
      localStorage.getItem(
        LAST_ACTIVITY_KEY
      );

    const parsed =
      stored
        ? Number(stored)
        : NaN;

    const lastActivity =
      Number.isFinite(parsed)
        ? parsed
        : Date.now();

    /*
     * User already 15 minutes idle tha aur
     * page dobara open/refresh hui.
     */
    if (
      Date.now() - lastActivity >=
      IDLE_TIMEOUT_MS
    ) {
      performLogout();
      return;
    }

    localStorage.setItem(
      LAST_ACTIVITY_KEY,
      String(lastActivity)
    );

    scheduleLogout(lastActivity);

    ACTIVITY_EVENTS.forEach(
      (eventName) => {
        window.addEventListener(
          eventName,
          recordActivity,
          {
            passive: true,
          }
        );
      }
    );

    /*
     * Browser/tab dobara visible hone par
     * timeout immediately verify karo.
     *
     * Important:
     * Tab par wapas aana automatically activity
     * nahi maana ja raha. Actual click/key/etc.
     * activity timer reset karegi.
     */
    const handleVisibilityChange =
      () => {
        if (
          document.visibilityState !==
          "visible"
        ) {
          return;
        }

        const latestStored =
          localStorage.getItem(
            LAST_ACTIVITY_KEY
          );

        const latestActivity =
          latestStored
            ? Number(latestStored)
            : NaN;

        if (
          !Number.isFinite(
            latestActivity
          ) ||
          Date.now() -
            latestActivity >=
            IDLE_TIMEOUT_MS
        ) {
          performLogout();
          return;
        }

        scheduleLogout(
          latestActivity
        );
      };

    /*
     * Another tab mein activity/logout hone par
     * current tab ko synchronize karo.
     */
    const handleStorage =
      (event: StorageEvent) => {
        if (
          event.key !==
          LAST_ACTIVITY_KEY
        ) {
          return;
        }

        if (!event.newValue) {
          return;
        }

        const latestActivity =
          Number(event.newValue);

        if (
          !Number.isFinite(
            latestActivity
          )
        ) {
          return;
        }

        scheduleLogout(
          latestActivity
        );
      };

    document.addEventListener(
      "visibilitychange",
      handleVisibilityChange
    );

    window.addEventListener(
      "storage",
      handleStorage
    );

    return () => {
      ACTIVITY_EVENTS.forEach(
        (eventName) => {
          window.removeEventListener(
            eventName,
            recordActivity
          );
        }
      );

      document.removeEventListener(
        "visibilitychange",
        handleVisibilityChange
      );

      window.removeEventListener(
        "storage",
        handleStorage
      );

      if (timeoutRef.current !== null) {
        window.clearTimeout(
          timeoutRef.current
        );

        timeoutRef.current = null;
      }
    };
  }, [
    user,
    performLogout,
    recordActivity,
    scheduleLogout,
  ]);

  /*
   * React Router navigation bhi genuine portal
   * activity hai.
   */
  useEffect(() => {
    if (user) {
      recordActivity();
    }
  }, [
    location.pathname,
    location.search,
    user,
    recordActivity,
  ]);

  return null;
}