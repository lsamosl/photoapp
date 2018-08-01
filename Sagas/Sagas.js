import { takeEvery, call } from 'redux-saga/effects';
import { autenticacion, baseDeDatos } from '../Servicios/Firebase';

const registroEnFirebase = (values) => {
  autenticacion
    .createUserWithEmailAndPassword(values.correo, values.password)
    .then(success => success.user.toJSON())
    .catch(error => error);
};

function* generadoraRegistro(values) {
  try {
    const registro = yield call(registroEnFirebase, values.datos);
    console.log(registro);
  } catch (error) {
    console.log(error);
  }
}

export default function* funcionPrimaria() {
  // yield ES6
  yield takeEvery('REGISTRO', generadoraRegistro);
  console.log('desde nuestra funcion generadora');
}
