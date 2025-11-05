#include <Wire.h>
#include <VL53L1X.h>

// ---- I²C pins ----
#define SDA_PIN   21
#define SCL_PIN   22

// ---- XSHUT pins ----
#define XSHUT_A   18   // Back sensor
#define XSHUT_B   19   // Calf sensor

// ---- I²C addresses ----
#define ADDR_A    0x2A
#define ADDR_B    0x29

// ---- Timing setup ----
const uint32_t I2C_HZ          = 10000;   // slow & stable for long wires
const uint16_t IM_PERIOD_MS    = 200;     // 5 Hz measurement cycle
const uint16_t TBUDGET_US      = 33000;
const uint16_t RECOVER_TIMEOUT = 1000;    // ms before recovery if no updates
const uint16_t PRINT_INTERVAL  = 300;     // print every 100 ms (10 Hz stream)
const uint16_t LOOP_DELAY_MS   = 5;       // CPU yield

VL53L1X A, B;
bool aOK = false, bOK = false;
uint32_t lastGoodA = 0, lastGoodB = 0;
int lastA = -1, lastB = -1;
uint32_t lastPrint = 0;

// ---------- Utility: clear stuck I²C bus ----------
void i2cBusClear() {
  pinMode(SDA_PIN, INPUT_PULLUP);
  pinMode(SCL_PIN, INPUT_PULLUP);
  delay(2);
  if (digitalRead(SDA_PIN) == LOW) {
    pinMode(SCL_PIN, OUTPUT);
    for (int i = 0; i < 9; i++) {
      digitalWrite(SCL_PIN, HIGH); delayMicroseconds(80);
      digitalWrite(SCL_PIN, LOW);  delayMicroseconds(80);
    }
    pinMode(SCL_PIN, INPUT_PULLUP);
  }
}

bool scanFor(uint8_t addr) {
  Wire.beginTransmission(addr);
  return Wire.endTransmission() == 0;
}

// ---------- Init both sensors ----------
bool initSensors() {
  digitalWrite(XSHUT_A, LOW);
  digitalWrite(XSHUT_B, LOW);
  delay(10);

  // Bring up A first, assign new address
  digitalWrite(XSHUT_A, HIGH);
  delay(10);
  A.setTimeout(500);
  if (!A.init()) return false;
  A.setAddress(ADDR_A);
  A.setDistanceMode(VL53L1X::Short);
  A.setMeasurementTimingBudget(TBUDGET_US);
  A.startContinuous(IM_PERIOD_MS);
  aOK = true; lastGoodA = millis();

  // Bring up B (default addr)
  digitalWrite(XSHUT_B, HIGH);
  delay(10);
  B.setTimeout(500);
  if (!B.init()) return false;
  B.setDistanceMode(VL53L1X::Short);
  B.setMeasurementTimingBudget(TBUDGET_US);
  B.startContinuous(IM_PERIOD_MS);
  bOK = true; lastGoodB = millis();

  return true;
}

// ---------- Full recovery ----------
bool recoverAll() {
  Wire.end();
  i2cBusClear();
  Wire.begin(SDA_PIN, SCL_PIN);
  Wire.setClock(I2C_HZ);
  return initSensors();
}

void setup() {
  Serial.begin(115200);
  delay(200);

  pinMode(SDA_PIN, INPUT_PULLUP);
  pinMode(SCL_PIN, INPUT_PULLUP);
  Wire.begin(SDA_PIN, SCL_PIN);
  Wire.setClock(I2C_HZ);

  pinMode(XSHUT_A, OUTPUT);
  pinMode(XSHUT_B, OUTPUT);

  if (!initSensors()) {
    Serial.println("Sensor init failed — check wiring.");
  }
}

void loop() {
  const uint32_t now = millis();

  // --- BACK sensor ---
  if (aOK && A.dataReady()) {
    uint16_t d = A.read(false);
    if (!A.timeoutOccurred()) {
      lastA = (int)d;
      lastGoodA = now;
    }
  }

  // --- CALF sensor ---
  if (bOK && B.dataReady()) {
    uint16_t d = B.read(false);
    if (!B.timeoutOccurred()) {
      lastB = (int)d;
      lastGoodB = now;
    }
  }

  // --- Auto-recover if any stale > timeout ---
  if ((now - lastGoodA > RECOVER_TIMEOUT) || (now - lastGoodB > RECOVER_TIMEOUT)) {
    aOK = bOK = false;
    recoverAll();
    return;
  }

  // --- Continuous stream: print latest valid values at steady rate ---
  if (now - lastPrint >= PRINT_INTERVAL) {
    lastPrint = now;

    // Only print if we’ve ever gotten a good sample (not startup)
    if (lastA != -1 || lastB != -1) {
      Serial.printf("A=%d,B=%d\n", lastA, lastB);
    }
  }

  delay(LOOP_DELAY_MS);
}
