import React, { Component } from 'react';
import {
  View, Text, Button, StyleSheet, TextInput,
} from 'react-native';
import { connect } from 'react-redux';
import SignUpForm from './Formas/SignUpForm';

class SignUp extends Component {
  registroDeUsuario = (values) => {
    // console.log(values);
    this.props.registro(values);
  }

  render() {
    // console.log(this.props.numero);
    const { navigation } = this.props;
    return (
      <View style={styles.containerComponent}>
        <SignUpForm registro={this.registroDeUsuario} />
        <Button title="SignIn" onPress={() => { navigation.goBack(); }} />
      </View>
    );
  }
}

const styles = StyleSheet.create({
  containerComponent: {
    flex: 1,
    justifyContent: 'center',
    backgroundColor: '#90EE90',
    paddingHorizontal: 16,
  },
});

const mapStateToProps = state => ({
  numero: state.reducerPrueba,
});

const mapDispatchToProps = dispatch => ({
  registro: (values) => {
    dispatch({ type: 'REGISTRO', datos: values });
  },
});

export default connect(mapStateToProps, mapDispatchToProps)(SignUp);
